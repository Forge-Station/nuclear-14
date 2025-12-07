using System.Linq;
using Content.Server.Popups;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Access.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed class StoreStructuredSystem : EntitySystem
{
    private const float AutoCloseDistance = 3f;
    private const float CheckInterval = 0.2f;

    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore");

    [Dependency] private readonly NcContractSystem _contracts = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    private readonly HashSet<EntityUid> _openStoreUids = new();

    [Dependency] private readonly PopupSystem _popups = default!;
    [Dependency] private readonly NcStoreSystem _storeSystem = default!;
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

        if (!_ui.HasUi(uid, StoreUiKey.Key))
            return;

        if (!_storeSystem.CanUseStore(uid, comp, user))
            return;

        if (comp.CurrentUser is { } current && current != user)
            return;

        if (TryComp(uid, out TransformComponent? storeXform) &&
            TryComp(user, out TransformComponent? userXform) &&
            !_xform.InRange(storeXform.Coordinates, userXform.Coordinates, AutoCloseDistance))
            return;

        var wasInUse = comp.CurrentUser != null;
        comp.CurrentUser = user;

        if (!wasInUse)
            _openStoreUids.Add(uid);

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            _ui.OpenUi(uid, StoreUiKey.Key, user);

        UpdateUiState(uid, comp, user);
    }

    private void OnUiClosed(EntityUid uid, NcStoreComponent comp, BoundUIClosedEvent ev)
    {
        if (!ev.UiKey.Equals(StoreUiKey.Key))
            return;

        comp.CurrentUser = null;
        _openStoreUids.Remove(uid);
    }

    private void OnUiRefreshRequest(EntityUid uid, NcStoreComponent comp, RequestUiRefreshMessage msg)
    {
        if (comp.CurrentUser is not { } user)
        {
            _openStoreUids.Remove(uid);
            return;
        }

        if (!_storeSystem.CanUseStore(uid, comp, user))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            _openStoreUids.Remove(uid);
            return;
        }

        UpdateUiState(uid, comp, user);
    }

    public void UpdateUiState(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        if (!_storeSystem.CanUseStore(uid, comp, user))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            _openStoreUids.Remove(uid);
            return;
        }

        UpdateContractsProgress(comp, user);

        var preferredCurrency = comp.CurrencyWhitelist.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(preferredCurrency))
        {
            preferredCurrency = comp.Listings
                .SelectMany(l => l.Cost.Keys)
                .FirstOrDefault();
        }

        var balance = string.IsNullOrWhiteSpace(preferredCurrency)
            ? 0
            : _logic.GetBalance(user, preferredCurrency);

        var listings = comp.Listings
            .Where(l => !string.IsNullOrEmpty(l.ProductEntity))
            .Select(l =>
            {
                string? currencyId = null;
                var priceF = 0f;

                if (!string.IsNullOrWhiteSpace(preferredCurrency) &&
                    l.Cost.TryGetValue(preferredCurrency, out var vPref))
                {
                    currencyId = preferredCurrency;
                    priceF = vPref;
                }
                else
                {
                    if (comp.CurrencyWhitelist.Count > 0)
                    {
                        var found = comp.CurrencyWhitelist.FirstOrDefault(c => l.Cost.ContainsKey(c));
                        if (!string.IsNullOrWhiteSpace(found))
                        {
                            currencyId = found;
                            priceF = l.Cost[found];
                        }
                    }

                    if (currencyId == null && l.Cost.Count > 0)
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

        Sawmill.Debug(
            $"[NcStore/ServerUI] {ToPrettyString(uid)}: Listings total={listings.Count}, Buy={buyCount}, Sell={sellCount}, Exchange={exchCount}");

        const string readyCat = "Готово к продаже";

        var readyToSell = listings
            .Where(d => d.Mode == StoreMode.Sell && d.Owned > 0 && d.Remaining != 0)
            .Select(d => new StoreListingData(
                d.Id + "__ready",
                d.ProductEntity,
                d.Price,
                readyCat,
                d.CurrencyId,
                d.Mode,
                d.Owned,
                d.Remaining
            ))
            .ToList();

        if (readyToSell.Count > 0)
            listings.AddRange(readyToSell);

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

                if (!string.IsNullOrWhiteSpace(preferredCurrency) &&
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
                if (owned <= 0)
                    continue;

                listings.Add(
                    new(
                        l.Id + "__crate",
                        l.ProductEntity,
                        price,
                        crateCat,
                        currencyId,
                        l.Mode,
                        owned,
                        l.RemainingCount
                    ));
            }
        }

        var contracts = comp.Contracts.Values
            .Select(c => new ContractClientData(
                c.Id,
                c.Name,
                c.TargetItem,
                c.Required,
                c.Progress,
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

        if (_openStoreUids.Count == 0)
            return;

        foreach (var uid in _openStoreUids.ToArray())
        {
            if (!TryComp(uid, out NcStoreComponent? store) ||
                !TryComp(uid, out TransformComponent? xform))
            {
                _openStoreUids.Remove(uid);
                continue;
            }

            if (store.CurrentUser is not { } userUid)
            {
                _openStoreUids.Remove(uid);
                continue;
            }

            if (!TryComp(userUid, out TransformComponent? userXform))
            {
                store.CurrentUser = null;
                _openStoreUids.Remove(uid);
                continue;
            }

            if (!_xform.InRange(xform.Coordinates, userXform.Coordinates, AutoCloseDistance))
            {
                _ui.CloseUi(uid, StoreUiKey.Key, userUid);
                store.CurrentUser = null;
                _openStoreUids.Remove(uid);
                continue;
            }

            if (!_storeSystem.CanUseStore(uid, store, userUid))
            {
                _ui.CloseUi(uid, StoreUiKey.Key, userUid);
                store.CurrentUser = null;
                _openStoreUids.Remove(uid);
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
            if (!_storeSystem.CanUseStore(uid, store, user))
            {
                _ui.CloseUi(uid, StoreUiKey.Key, user);
                store.CurrentUser = null;
                _openStoreUids.Remove(uid);
            }
        }
    }

    private void OnUserEntInserted(
        EntityUid uid,
        ContainerManagerComponent comp,
        ref EntInsertedIntoContainerMessage args
    )
    {
        if (_openStoreUids.Count == 0)
            return;

        _logic.InvalidateAllInventoryCache();
        RefreshAllOpenStores();
    }

    private void OnUserEntRemoved(
        EntityUid uid,
        ContainerManagerComponent comp,
        ref EntRemovedFromContainerMessage args
    )
    {
        if (_openStoreUids.Count == 0)
            return;

        _logic.InvalidateAllInventoryCache();
        RefreshAllOpenStores();
    }


    private void OnStackCountChanged(
        EntityUid uid,
        StackComponent comp,
        ref StackCountChangedEvent args
    )
    {
        if (_openStoreUids.Count == 0)
            return;

        _logic.InvalidateAllInventoryCache();
        RefreshAllOpenStores();
    }

    private void RefreshAllOpenStores()
    {
        if (_openStoreUids.Count == 0)
            return;

        foreach (var uid in _openStoreUids.ToArray())
        {
            if (!TryComp<NcStoreComponent>(uid, out var store))
            {
                _openStoreUids.Remove(uid);
                continue;
            }

            if (store.CurrentUser is not { } user)
            {
                _openStoreUids.Remove(uid);
                continue;
            }

            UpdateUiState(uid, store, user);
        }
    }

    private void OnClaimContract(EntityUid uid, NcStoreComponent comp, ClaimContractBoundMessage msg)
    {
        if (comp.CurrentUser is not { } user)
            return;

        UpdateContractsProgress(comp, user);

        if (!_contracts.TryClaim(uid, user, msg.ContractId))
            return;

        _popups.PopupEntity("Контракт выполнен!", uid, user);

        UpdateUiState(uid, comp, user);
    }

    private void UpdateContractsProgress(NcStoreComponent comp, EntityUid user)
    {
        if (comp.Contracts.Count == 0)
            return;

        EntityUid? crate = null;

        if (TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulled &&
            TryComp(pulled, out EntityStorageComponent? storage) &&
            !storage.Open)
            crate = pulled;

        foreach (var (_, contract) in comp.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contract.TargetItem))
            {
                contract.Progress = 0;
                continue;
            }

            var owned = _logic.GetOwned(user, contract.TargetItem);

            if (crate is { } crateUid)
                owned += _logic.GetOwnedInRoot(crateUid, contract.TargetItem);

            contract.Progress = Math.Min(owned, contract.Required);
        }
    }
}
