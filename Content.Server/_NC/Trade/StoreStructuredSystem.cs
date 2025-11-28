using System.Linq;
using Content.Server.Popups;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed class StoreStructuredSystem : EntitySystem
{
    private const float AutoCloseDistance = 3f;
    private const float CheckInterval = 0.2f;

    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore");

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly NcContractSystem _contracts = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly PopupSystem _popups = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private TimeSpan _nextCheck = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NcStoreComponent, ActivatableUIOpenAttemptEvent>(
            OnUiOpenAttempt,
            new[] { typeof(ActivatableUISystem), });

        SubscribeLocalEvent<NcStoreComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<NcStoreComponent, RequestUiRefreshMessage>(OnUiRefreshRequest);
        SubscribeLocalEvent<AccessReaderComponent, AccessReaderConfigurationChangedEvent>(OnAccessReaderChanged);
        SubscribeLocalEvent<ContainerManagerComponent, EntInsertedIntoContainerMessage>(OnUserEntInserted);
        SubscribeLocalEvent<ContainerManagerComponent, EntRemovedFromContainerMessage>(OnUserEntRemoved);
        SubscribeLocalEvent<StackComponent, StackCountChangedEvent>(OnStackCountChanged);
        SubscribeLocalEvent<NcStoreComponent, ClaimContractBoundMessage>(OnClaimContract);
    }


    private void OnUiOpenAttempt(EntityUid uid, NcStoreComponent comp, ref ActivatableUIOpenAttemptEvent ev)
    {
        ev.Cancel();
        var user = ev.User;

        Logger.Info($"[NcStore] Open attempt on {uid} by {user}");

        if (!_ui.HasUi(uid, StoreUiKey.Key))
        {
            Logger.Error($"[NcStore] UI not found! UserInterface key=Key is missing on entity {uid}");
            return;
        }

        if (!IsAccessAllowed(uid, comp, user))
        {
            Logger.Warning($"[NcStore] Access denied for {user} to store {uid}");
            return;
        }

        if (comp.CurrentUser is { } current && current != user)
        {
            Logger.Warning($"[NcStore] Store {uid} is already used by {current}");
            return;
        }

        if (TryComp(uid, out TransformComponent? storeXform) &&
            TryComp(user, out TransformComponent? userXform) &&
            !_xform.InRange(storeXform.Coordinates, userXform.Coordinates, AutoCloseDistance))
        {
            Logger.Warning($"[NcStore] Too far: user {user} cannot open store {uid}");
            return;
        }

        Logger.Info($"[NcStore] Opening UI for {user}…");

        comp.CurrentUser = user;

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            _ui.OpenUi(uid, StoreUiKey.Key, user);

        UpdateUiState(uid, comp, user);
    }


    private void OnUiClosed(EntityUid uid, NcStoreComponent comp, BoundUIClosedEvent ev)
    {
        if (ev.UiKey.Equals(StoreUiKey.Key))
            comp.CurrentUser = null;
    }

    private void OnUiRefreshRequest(EntityUid uid, NcStoreComponent comp, RequestUiRefreshMessage msg)
    {
        if (comp.CurrentUser is not { } user)
            return;

        if (!IsAccessAllowed(uid, comp, user))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            return;
        }

        UpdateUiState(uid, comp, user);
    }

    public void UpdateUiState(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        if (!IsAccessAllowed(uid, comp, user))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            return;
        }

        UpdateContractsProgress(comp, user);

        var preferredCurrency = comp.CurrencyWhitelist.FirstOrDefault();
        var balance = string.IsNullOrEmpty(preferredCurrency)
            ? 0
            : _logic.GetBalance(user, preferredCurrency);

        var listings = comp.Listings
            .Where(l => !string.IsNullOrEmpty(l.ProductEntity))
            .Select(l =>
            {
                string? currencyId = null;
                var priceF = 0f;

                if (!string.IsNullOrEmpty(preferredCurrency) &&
                    l.Cost.TryGetValue(preferredCurrency, out var vPref))
                {
                    currencyId = preferredCurrency;
                    priceF = vPref;
                }
                else
                {
                    var found = comp.CurrencyWhitelist.FirstOrDefault(c => l.Cost.ContainsKey(c));
                    if (!string.IsNullOrEmpty(found))
                    {
                        currencyId = found;
                        priceF = l.Cost[found];
                    }
                    else if (l.Cost.Count > 0)
                    {
                        var kv = l.Cost.First();
                        currencyId = kv.Key;
                        priceF = kv.Value;
                    }
                }

                var price = (int) MathF.Ceiling(priceF);
                var cat = l.Categories.Count > 0 ? l.Categories[0] : "Разное";

                int owned;
                try
                {
                    owned = _logic.GetOwned(user, l.ProductEntity);
                }
                catch
                {
                    owned = 0;
                }

                return new StoreListingData(
                    l.Id,
                    l.ProductEntity,
                    price,
                    cat,
                    currencyId ?? string.Empty,
                    l.Mode,
                    owned,
                    l.RemainingCount
                );
            })
            .ToList();

        var buyCount = listings.Count(x => x.Mode == StoreMode.Buy);
        var sellCount = listings.Count(x => x.Mode == StoreMode.Sell);
        var exchCount = listings.Count(x => x.Mode == StoreMode.Exchange);

        Sawmill.Info(
            $"[NcStore/ServerUI] {ToPrettyString(uid)}: Listings total={listings.Count}, Buy={buyCount}, Sell={sellCount}, Exchange={exchCount}");


        // ─── "Готово к продаже" ───
        const string readyCat = "Готово к продаже";

        var readyToSell = listings
            .Where(d => d.Mode == StoreMode.Sell && d.Owned > 0 && d.Remaining != 0)
            .Select(d => new StoreListingData
            {
                Id = d.Id,
                ProductEntity = d.ProductEntity,
                Price = d.Price,
                Category = readyCat,
                CurrencyId = d.CurrencyId,
                Mode = d.Mode,
                Owned = d.Owned,
                Remaining = d.Remaining
            })
            .ToList();


        if (readyToSell.Count > 0)
            listings.AddRange(readyToSell);

        // ─── Массовая продажа из ящика ───
        var crateTotals = new Dictionary<string, int>();
        const string crateCat = "Готово к продаже в ящике";

        EntityUid? crate = null;

        if (TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulled &&
            TryComp(pulled, out EntityStorageComponent? storage) &&
            !storage.Open)
            crate = pulled;

        if (crate is { } crateUid)
        {
            crateTotals = _logic.GetMassSellValue(comp, crateUid);

            var crateListings = comp.Listings
                .Where(l => l.Mode == StoreMode.Sell && !string.IsNullOrEmpty(l.ProductEntity))
                .ToList();

            foreach (var l in crateListings)
            {
                int countInCrate;
                try
                {
                    countInCrate = _logic.GetOwnedInRoot(crateUid, l.ProductEntity);
                }
                catch
                {
                    continue;
                }

                if (countInCrate <= 0 || l.RemainingCount == 0)
                    continue;

                string? currencyId = null;
                var priceF = 0f;

                if (!string.IsNullOrEmpty(preferredCurrency) &&
                    l.Cost.TryGetValue(preferredCurrency, out var vPref2))
                {
                    currencyId = preferredCurrency;
                    priceF = vPref2;
                }
                else if (l.Cost.Count > 0)
                {
                    var kv = l.Cost.First();
                    currencyId = kv.Key;
                    priceF = kv.Value;
                }

                var price = (int) MathF.Ceiling(priceF);
                if (price <= 0 || currencyId == null)
                    continue;

                var maxByRemaining = l.RemainingCount >= 0 ? l.RemainingCount : int.MaxValue;
                var owned = Math.Min(countInCrate, maxByRemaining);

                listings.Add(
                    new()
                    {
                        Id = l.Id + "__crate",
                        ProductEntity = l.ProductEntity,
                        Price = price,
                        Category = crateCat,
                        CurrencyId = currencyId,
                        Mode = l.Mode,
                        Owned = owned,
                        Remaining = l.RemainingCount
                    });
            }
        }

        var contracts = comp.Contracts.Values
            .Select(c => new ContractClientData(
                c.Id,
                c.TargetItem,
                c.Progress,
                c.Required,
                c.Reward,
                c.RewardCurrency,
                c.RewardItem,
                c.RewardItemCount,
                c.Difficulty,
                c.Completed,
                c.Description
            ))
            .ToList();

        _ui.SetUiState(uid, StoreUiKey.Key, new StoreUiState(balance, listings, crateTotals, contracts));

    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(CheckInterval);

        var iter = EntityQueryEnumerator<NcStoreComponent, TransformComponent>();
        while (iter.MoveNext(out var uid, out var store, out var xform))
        {
            if (store.CurrentUser is not { } userUid)
                continue;

            if (!EntityManager.TryGetComponent(userUid, out TransformComponent? userXform))
            {
                store.CurrentUser = null;
                continue;
            }

            if (!_xform.InRange(xform.Coordinates, userXform.Coordinates, AutoCloseDistance))
            {
                _ui.CloseUi(uid, StoreUiKey.Key, userUid);
                store.CurrentUser = null;
                continue;
            }

            if (!IsAccessAllowed(uid, store, userUid))
            {
                _ui.CloseUi(uid, StoreUiKey.Key, userUid);
                store.CurrentUser = null;
                _popups.PopupEntity(Loc.GetString("ncstore-no-access"), uid, userUid);
            }
        }
    }

    private void OnAccessReaderChanged(
        EntityUid uid,
        AccessReaderComponent comp,
        ref AccessReaderConfigurationChangedEvent args
    )
    {
        if (TryComp<NcStoreComponent>(uid, out var store) && store.CurrentUser is { } user)
        {
            if (!IsAccessAllowed(uid, store, user))
            {
                _ui.CloseUi(uid, StoreUiKey.Key, user);
                store.CurrentUser = null;
            }
        }
    }


    private void OnUserEntInserted(
        EntityUid uid,
        ContainerManagerComponent comp,
        ref EntInsertedIntoContainerMessage args
    ) =>
        RefreshAllOpenStores();

    private void OnUserEntRemoved(
        EntityUid uid,
        ContainerManagerComponent comp,
        ref EntRemovedFromContainerMessage args
    ) =>
        RefreshAllOpenStores();


    private void OnStackCountChanged(
        EntityUid uid,
        StackComponent comp,
        ref StackCountChangedEvent args
    ) =>
        RefreshAllOpenStores();

    private void RefreshAllOpenStores()
    {
        var query = EntityQueryEnumerator<NcStoreComponent>();
        while (query.MoveNext(out var storeUid, out var storeComp))
            if (storeComp.CurrentUser is { } user)
                UpdateUiState(storeUid, storeComp, user);
    }

    private void OnClaimContract(EntityUid uid, NcStoreComponent comp, ClaimContractBoundMessage msg)
    {
        if (comp.CurrentUser is not { } user)
            return;

        UpdateContractsProgress(comp, user);

        if (!_contracts.TryClaim(uid, user, msg.ContractId))
            return;

        UpdateUiState(uid, comp, user);
    }

    private void UpdateContractsProgress(NcStoreComponent comp, EntityUid user)
    {
        if (comp.Contracts.Count == 0)
            return;

        foreach (var (_, contract) in comp.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contract.TargetItem))
            {
                contract.Progress = 0;
                continue;
            }

            var owned = _logic.GetOwned(user, contract.TargetItem);

            contract.Progress = Math.Min(owned, contract.Required);
        }
    }


    private bool IsAccessAllowed(EntityUid storeUid, NcStoreComponent comp, EntityUid user)
    {
        if (TryComp<AccessReaderComponent>(storeUid, out var reader))
            return _access.IsAllowed(user, storeUid, reader);

        if (comp.Access is { Count: > 0, })
        {
            var fake = new AccessReaderComponent();
            fake.AccessLists.Clear();

            foreach (var group in comp.Access)
            {
                var set = new HashSet<ProtoId<AccessLevelPrototype>>();
                foreach (var token in group)
                {
                    if (_prototypeManager.TryIndex<AccessLevelPrototype>(token, out _))
                    {
                        set.Add(new(token));
                        continue;
                    }

                    if (_prototypeManager.TryIndex<AccessGroupPrototype>(token, out var grp))
                    {
                        if (grp.Tags.Count == 0)
                        {
                            Sawmill.Warning(
                                $"[Access] Empty access group '{token}' on {ToPrettyString(storeUid)}; skipping.");
                            continue;
                        }

                        if (set.Count > 0)
                        {
                            fake.AccessLists.Add(set);
                            set = new();
                        }

                        foreach (var lvl in grp.Tags)
                            fake.AccessLists.Add(new() { lvl, });

                        continue;
                    }

                    Sawmill.Warning(
                        $"[Access] Unknown access token '{token}' on {ToPrettyString(storeUid)}; skipping.");
                }

                if (set.Count > 0)
                    fake.AccessLists.Add(set);
            }

            if (fake.AccessLists.Count == 0)
            {
                Sawmill.Warning($"[Access] All access groups invalid/empty on {ToPrettyString(storeUid)}; denying.");
                return false;
            }

            return _access.IsAllowed(user, storeUid, fake);
        }

        return true;
    }
}
