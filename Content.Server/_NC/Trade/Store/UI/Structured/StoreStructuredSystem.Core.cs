using System.Linq;
using Content.Server.Popups;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Access.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed partial class StoreStructuredSystem : EntitySystem
{
    private const float AutoCloseDistance = 3f;
    private const float MinAccelInterval = 0.25f;
    private const float MinDynamicInterval = 0.25f;
    private const int WatchedRootSearchLimit = 32;
    private const float CheckInterval = 1.0f;
    private readonly HashSet<EntityUid> _affectedStoresScratch = new();
    [Dependency] private readonly AudioSystem _audio = default!;
    private readonly Dictionary<EntityUid, (int Revision, List<StoreListingStaticData> List)> _catalogCache = new();
    [Dependency] private readonly NcContractSystem _contracts = default!;
    private readonly NcInventorySnapshot _crateSnapScratch = new();
    private readonly List<EntityUid> _deepCrateItemsScratch = new();
    private readonly List<EntityUid> _deepUserItemsScratch = new();
    private readonly HashSet<EntityUid> _dirtyStores = new();
    private readonly List<EntityUid> _dirtyStoresScratch = new();
    private readonly Dictionary<EntityUid, DynamicScratch> _dynamicScratchByStore = new();
    [Dependency] private readonly StoreSystemStructuredLoader _loader = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    private readonly List<EntityUid> _openStoresScratch = new();
    private readonly HashSet<EntityUid> _openStoreUids = new();
    private readonly HashSet<EntityUid> _pendingRefreshEntities = new();
    [Dependency] private readonly PopupSystem _popups = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _storesByWatchedRoot = new();
    [Dependency] private readonly NcStoreSystem _storeSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    private readonly NcInventorySnapshot _userSnapScratch = new();
    private readonly Dictionary<EntityUid, (EntityUid User, EntityUid? Crate)> _watchByStore = new();

    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private TimeSpan _nextAccelAllowed = TimeSpan.Zero;
    private TimeSpan _nextCheck = TimeSpan.Zero;

    private TimeSpan _nextDynamicAllowed = TimeSpan.Zero;

    private DynamicScratch GetDynamicScratch(EntityUid storeUid)
    {
        if (_dynamicScratchByStore.TryGetValue(storeUid, out var scratch))
            return scratch;

        scratch = new();
        _dynamicScratchByStore[storeUid] = scratch;
        return scratch;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NcStoreComponent, ActivatableUIOpenAttemptEvent>(OnUiOpenAttempt);
        SubscribeLocalEvent<NcStoreComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<NcStoreComponent, RequestUiRefreshMessage>(OnUiRefreshRequest);
        SubscribeLocalEvent<AccessReaderComponent, AccessReaderConfigurationChangedEvent>(OnAccessReaderChanged);
        SubscribeLocalEvent<NcStoreComponent, ComponentShutdown>(OnStoreShutdown);
        SubscribeLocalEvent<ContainerManagerComponent, EntInsertedIntoContainerMessage>(OnUserEntInserted);
        SubscribeLocalEvent<ContainerManagerComponent, EntRemovedFromContainerMessage>(OnUserEntRemoved);
        SubscribeLocalEvent<StackComponent, StackCountChangedEvent>(OnStackCountChanged);
        SubscribeLocalEvent<NcStoreComponent, ClaimContractBoundMessage>(OnClaimContract);
        SubscribeLocalEvent<EntityStorageComponent, StorageAfterOpenEvent>(OnStorageOpen);
        SubscribeLocalEvent<EntityStorageComponent, StorageAfterCloseEvent>(OnStorageClose);
    }

    private void OnStorageOpen(EntityUid uid, EntityStorageComponent comp, ref StorageAfterOpenEvent args)
    {
        if (_storesByWatchedRoot.ContainsKey(uid))
            RefreshStoresAffectedBy(uid);
    }

    private void OnStorageClose(EntityUid uid, EntityStorageComponent comp, ref StorageAfterCloseEvent args)
    {
        if (_storesByWatchedRoot.ContainsKey(uid))
            RefreshStoresAffectedBy(uid);
    }

    private void OnStoreShutdown(EntityUid uid, NcStoreComponent comp, ComponentShutdown args)
    {
        _catalogCache.Remove(uid);
        _dynamicScratchByStore.Remove(uid);

        if (_openStoreUids.Contains(uid) || _watchByStore.ContainsKey(uid) || _dirtyStores.Contains(uid))
        {
            EntityUid? user = null;

            if (_watchByStore.TryGetValue(uid, out var watch) && watch.User != EntityUid.Invalid)
                user = watch.User;
            else if (comp.CurrentUser is { } cur && cur != EntityUid.Invalid)
                user = cur;

            CloseAndCleanUp(uid, user);
        }
    }

    public void RefreshCatalog(EntityUid uid, NcStoreComponent comp)
    {
        _catalogCache.Remove(uid);
        _dynamicScratchByStore.Remove(uid);

        comp.BumpCatalogRevision();

        if (comp.CurrentUser is not { } user)
            return;

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            return;

        SendCatalog(uid, comp, user);
        UpdateDynamicState(uid, comp, user);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        ProcessPendingRefreshes();

        if (_openStoreUids.Count > 0)
        {
            _openStoresScratch.Clear();
            _openStoresScratch.AddRange(_openStoreUids);

            foreach (var uid in _openStoresScratch)
            {
                if (!TryComp(uid, out NcStoreComponent? store) || store.CurrentUser is not { } user)
                    continue;

                if (EnsureCrateWatchUpToDate(uid, user))
                    MarkDirty(uid);
            }
        }

        if (_dirtyStores.Count > 0 && _timing.CurTime >= _nextDynamicAllowed)
        {
            _dirtyStoresScratch.Clear();
            _dirtyStoresScratch.AddRange(_dirtyStores);
            _dirtyStores.Clear();

            foreach (var uid in _dirtyStoresScratch)
                if (TryComp(uid, out NcStoreComponent? store) && store.CurrentUser is { } user)
                    UpdateDynamicState(uid, store, user);

            _nextDynamicAllowed = _timing.CurTime + TimeSpan.FromSeconds(MinDynamicInterval);
        }

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(CheckInterval);

        if (_openStoreUids.Count == 0)
            return;
        _openStoresScratch.Clear();
        _openStoresScratch.AddRange(_openStoreUids);

        foreach (var uid in _openStoresScratch)
        {
            if (!TryComp(uid, out NcStoreComponent? store) || !TryComp(uid, out TransformComponent? xform))
            {
                CloseAndCleanUp(uid);
                continue;
            }

            if (store.CurrentUser is not { } userUid)
            {
                CloseAndCleanUp(uid);
                continue;
            }

            if (!TryComp(userUid, out TransformComponent? userXform) ||
                !_xform.InRange(xform.Coordinates, userXform.Coordinates, AutoCloseDistance))
            {
                CloseAndCleanUp(uid, userUid);
                store.CurrentUser = null;
                continue;
            }

            if (!_storeSystem.CanUseStore(uid, store, userUid))
            {
                CloseAndCleanUp(uid, userUid);
                store.CurrentUser = null;
                _popups.PopupEntity(Loc.GetString("nc-store-no-access"), uid, userUid);
            }
        }
    }

    private void CloseAndCleanUp(EntityUid storeUid, EntityUid? user = null)
    {
        if (_watchByStore.TryGetValue(storeUid, out var info))
        {
            if (info.User != EntityUid.Invalid)
                _logic.InvalidateInventoryCache(info.User);

            if (info.Crate is { } crate)
                _logic.InvalidateInventoryCache(crate);
        }

        if (user != null)
            _ui.CloseUi(storeUid, StoreUiKey.Key, user.Value);
        _openStoreUids.Remove(storeUid);
        UnregisterStoreWatch(storeUid);
        _dirtyStores.Remove(storeUid);
        _dynamicScratchByStore.Remove(storeUid);
    }

    private bool EnsureCrateWatchUpToDate(EntityUid storeUid, EntityUid user)
    {
        EntityUid? crateUid = null;
        if (_logic.TryGetPulledClosedCrate(user, out var pulledCrate))
            crateUid = pulledCrate;
        if (_watchByStore.TryGetValue(storeUid, out var prev))
        {
            if (prev.User == user && prev.Crate == crateUid)
                return false;
            if (prev.Crate != crateUid)
            {
                if (prev.Crate is { } oldCrate)
                    _logic.InvalidateInventoryCache(oldCrate);
                if (crateUid is { } newCrate)
                    _logic.InvalidateInventoryCache(newCrate);
            }

            if (prev.User != user)
            {
                if (prev.User != EntityUid.Invalid)
                    _logic.InvalidateInventoryCache(prev.User);
                _logic.InvalidateInventoryCache(user);
            }
        }
        else
        {
            _logic.InvalidateInventoryCache(user);
            if (crateUid is { } newCrate)
                _logic.InvalidateInventoryCache(newCrate);
        }

        UpdateStoreWatch(storeUid, user, crateUid);
        return true;
    }

    private void AddWatchedRoot(EntityUid root, EntityUid storeUid)
    {
        if (!_storesByWatchedRoot.TryGetValue(root, out var set))
        {
            set = new();
            _storesByWatchedRoot[root] = set;
        }

        set.Add(storeUid);
    }

    private void RemoveWatchedRoot(EntityUid root, EntityUid storeUid)
    {
        if (!_storesByWatchedRoot.TryGetValue(root, out var set))
            return;
        set.Remove(storeUid);
        if (set.Count == 0)
            _storesByWatchedRoot.Remove(root);
    }

    private void UpdateStoreWatch(EntityUid storeUid, EntityUid user, EntityUid? crate)
    {
        if (user == EntityUid.Invalid)
        {
            UnregisterStoreWatch(storeUid);
            return;
        }

        if (_watchByStore.TryGetValue(storeUid, out var prev))
        {
            if (prev.User == user && prev.Crate == crate)
                return;
            if (prev.User != EntityUid.Invalid)
                RemoveWatchedRoot(prev.User, storeUid);
            if (prev.Crate is { } oldCrate)
                RemoveWatchedRoot(oldCrate, storeUid);
        }

        _watchByStore[storeUid] = (user, crate);
        AddWatchedRoot(user, storeUid);
        _logic.InvalidateInventoryCache(user);
        if (crate is { } c)
        {
            AddWatchedRoot(c, storeUid);
            _logic.InvalidateInventoryCache(c);
        }
    }

    private void UnregisterStoreWatch(EntityUid storeUid)
    {
        if (!_watchByStore.TryGetValue(storeUid, out var info))
            return;
        if (info.User != EntityUid.Invalid)
            RemoveWatchedRoot(info.User, storeUid);
        if (info.Crate is { } crate)
            RemoveWatchedRoot(crate, storeUid);
        _watchByStore.Remove(storeUid);
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
        if (TryComp(uid, out TransformComponent? sX) && TryComp(user, out TransformComponent? uX) &&
            !_xform.InRange(sX.Coordinates, uX.Coordinates, AutoCloseDistance))
            return;

        var wasInUse = comp.CurrentUser != null;
        comp.CurrentUser = user;
        if (!wasInUse)
            _openStoreUids.Add(uid);

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            _ui.OpenUi(uid, StoreUiKey.Key, user);

        EnsureCrateWatchUpToDate(uid, user);

        _loader.EnsureLoaded(uid, comp, "UiOpenAttempt");

        SendCatalog(uid, comp, user);
        UpdateDynamicState(uid, comp, user);
    }


    private void SendCatalog(EntityUid store, NcStoreComponent comp, EntityUid user)
    {
        if (!_ui.IsUiOpen(store, StoreUiKey.Key, user))
            return;

        if (_catalogCache.TryGetValue(store, out var cached) && cached.Revision == comp.CatalogRevision)
        {
            var cachedList = cached.List;

            var hasBuy = false;
            var hasSell = false;

            foreach (var l in cachedList)
            {
                if (l.Mode == StoreMode.Buy)
                    hasBuy = true;
                else if (l.Mode == StoreMode.Sell)
                    hasSell = true;

                if (hasBuy && hasSell)
                    break;
            }

            var msg = new StoreCatalogMessage(
                comp.CatalogRevision,
                cachedList,
                hasBuy,
                hasSell,
                comp.ContractPresets.Count > 0 || !string.IsNullOrWhiteSpace(comp.LegacyContractsPreset)
            );
            _ui.ServerSendUiMessage((store, null), StoreUiKey.Key, msg, user);
            return;
        }


        var list = new List<StoreListingStaticData>(comp.Listings.Count);

        foreach (var l in comp.Listings)
        {
            if (string.IsNullOrWhiteSpace(l.Id) || string.IsNullOrWhiteSpace(l.ProductEntity))
                continue;

            var cat = l.Categories.Count > 0 ? l.Categories[0] : Loc.GetString("nc-store-category-fallback");

            if (!TryPickUiCurrencyAndPrice(comp, l, out var cur, out var price))
                continue;

            list.Add(
                new(
                    l.Id,
                    l.Mode,
                    cat,
                    l.ProductEntity,
                    price,
                    cur
                ));
        }

        _catalogCache[store] = (comp.CatalogRevision, list);

        {
            var hasBuy = false;
            var hasSell = false;

            foreach (var l in list)
            {
                if (l.Mode == StoreMode.Buy)
                    hasBuy = true;
                else if (l.Mode == StoreMode.Sell)
                    hasSell = true;

                if (hasBuy && hasSell)
                    break;
            }

            var msg = new StoreCatalogMessage(
                comp.CatalogRevision,
                list,
                hasBuy,
                hasSell,
                comp.ContractPresets.Count > 0 || !string.IsNullOrWhiteSpace(comp.LegacyContractsPreset)
            );

            _ui.ServerSendUiMessage((store, null), StoreUiKey.Key, msg, user);
        }
    }

    private void OnUiClosed(EntityUid uid, NcStoreComponent comp, BoundUIClosedEvent ev)
    {
        if (!ev.UiKey.Equals(StoreUiKey.Key))
            return;
        comp.CurrentUser = null;
        CloseAndCleanUp(uid);
    }

    private void OnUiRefreshRequest(EntityUid uid, NcStoreComponent comp, RequestUiRefreshMessage msg)
    {
        if (comp.CurrentUser is not { } user)
        {
            CloseAndCleanUp(uid);
            return;
        }

        if (!_storeSystem.CanUseStore(uid, comp, user))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            CloseAndCleanUp(uid);
            return;
        }

        EnsureCrateWatchUpToDate(uid, user);
        UpdateDynamicState(uid, comp, user);
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
                CloseAndCleanUp(uid);
            }
        }
    }

    private void OnClaimContract(EntityUid uid, NcStoreComponent comp, ClaimContractBoundMessage msg)
    {
        if (comp.CurrentUser is not { } user)
            return;
        if (_contracts.TryClaim(uid, user, msg.ContractId))
        {
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), user);
            _popups.PopupEntity(Loc.GetString("nc-store-contract-completed"), uid, user);
            UpdateDynamicState(uid, comp, user);
            return;
        }

        _logic.InvalidateInventoryCache(user);
        NcInventorySnapshot? crateSnap = null;
        EntityUid crate = default;
        if (_logic.TryGetPulledClosedCrate(user, out crate))
            _logic.InvalidateInventoryCache(crate);
        var userSnap = _logic.BuildInventorySnapshot(user);
        if (crate != default)
            crateSnap = _logic.BuildInventorySnapshot(crate);

        UpdateContractsProgress(comp, userSnap, crateSnap);
    }

    private void MarkDirty(EntityUid storeUid)
    {
        if (storeUid != EntityUid.Invalid)
            _dirtyStores.Add(storeUid);
    }

    private bool TryPickUiCurrencyAndPrice(
        NcStoreComponent comp,
        NcStoreListingDef listing,
        out string currencyId,
        out int price
    )
    {
        currencyId = string.Empty;
        price = 0;
        if (listing.Cost.Count == 0)
            return false;
        foreach (var cur in comp.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;
            if (listing.Cost.TryGetValue(cur, out var pf))
            {
                var p = (int) MathF.Ceiling(pf);
                if (p > 0)
                {
                    currencyId = cur;
                    price = p;
                    return true;
                }
            }
        }

        var first = listing.Cost.First();
        var fp = (int) MathF.Ceiling(first.Value);
        if (fp > 0 && !string.IsNullOrWhiteSpace(first.Key))
        {
            currencyId = first.Key;
            price = fp;
            return true;
        }

        return false;
    }

    private ContractClientData MapContractToClient(ContractServerData c)
    {
        var targets = new List<ContractTargetClientData>();

        if (c.Targets is { Count: > 0 })
        {
            foreach (var t in c.Targets)
            {
                if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
                    continue;

                targets.Add(
                    new(t.TargetItem, t.Required, t.Progress)
                    {
                        MatchMode = t.MatchMode
                    });
            }
        }
        else if (!string.IsNullOrWhiteSpace(c.TargetItem) && c.Required > 0)
        {
            targets.Add(
                new(c.TargetItem, c.Required, c.Progress)
                {
                    MatchMode = c.MatchMode
                });
        }

        var rewards = c.Rewards is { Count: > 0 }
            ? new(c.Rewards)
            : new List<ContractRewardData>();

        return new(
            c.Id,
            c.Name,
            c.Difficulty,
            c.Description,
            c.Repeatable,
            c.Completed,
            c.TargetItem,
            c.Required,
            c.Progress,
            targets,
            rewards
        );
    }
    private sealed class DynamicScratch
    {
        private readonly DynamicStateBuffer[] _buffers = { new(), new() };
        private int _activeIndex;

        public DynamicStateBuffer GetWriteBuffer()
        {
            return _buffers[1 - _activeIndex];
        }

        public DynamicStateBuffer GetReadBuffer()
        {
            return _buffers[_activeIndex];
        }

        public void Commit()
        {
            _activeIndex = 1 - _activeIndex;
        }
    }

    private sealed class DynamicStateBuffer
    {
        public readonly Dictionary<string, int> BalancesByCurrency = new();
        public readonly Dictionary<string, int> RemainingById = new();
        public readonly Dictionary<string, int> OwnedById = new();
        public readonly Dictionary<string, int> CrateUnitsById = new();
        public readonly Dictionary<string, int> CrateTotals = new();
        public readonly List<ContractClientData> Contracts = new();
        public int ContractsHash;

        public void Clear()
        {
            BalancesByCurrency.Clear();
            RemainingById.Clear();
            OwnedById.Clear();
            CrateUnitsById.Clear();
            CrateTotals.Clear();
            Contracts.Clear();
            ContractsHash = 0;
        }
    }
}
