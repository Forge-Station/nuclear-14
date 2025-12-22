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
    private const float MinAccelInterval = 0.05f;
    private const int WatchedRootSearchLimit = 32;
    private readonly HashSet<EntityUid> _affectedStoresScratch = new();
    private readonly Dictionary<EntityUid, List<StoreListingStaticData>> _catalogCache = new();
    [Dependency] private readonly NcContractSystem _contracts = default!;
    private readonly HashSet<EntityUid> _dirtyStores = new();
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    private readonly List<EntityUid> _openStoresScratch = new();
    private readonly HashSet<EntityUid> _openStoreUids = new();
    private readonly HashSet<EntityUid> _pendingRefreshEntities = new();
    [Dependency] private readonly PopupSystem _popups = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _storesByWatchedRoot = new();
    [Dependency] private readonly NcStoreSystem _storeSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private readonly Dictionary<EntityUid, (EntityUid User, EntityUid? Crate)> _watchByStore = new();
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private TimeSpan _nextAccelAllowed = TimeSpan.Zero;
    private TimeSpan _nextCheck = TimeSpan.Zero;

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
    }

    private void OnStoreShutdown(EntityUid uid, NcStoreComponent comp, ComponentShutdown args) =>
        _catalogCache.Remove(uid);

    public void RefreshCatalog(EntityUid uid, NcStoreComponent comp)
    {
        _catalogCache.Remove(uid);

        comp.BumpCatalogRevision();

        if (comp.CurrentUser is not { } user)
            return;

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            return;

        SendCatalog(uid, comp, user);
        UpdateDynamicState(uid, comp, user);
    }


    public void UpdateDynamicState(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        EntityUid? crateUid = null;
        if (TryGetPulledClosedCrate(user, out var pulledCrate))
            crateUid = pulledCrate;

        UpdateStoreWatch(uid, user, crateUid);

        var userSnap = _logic.BuildInventorySnapshot(user);

        NcStoreLogicSystem.InventorySnapshot? crateSnap = null;
        if (crateUid is { } crateEntity)
            crateSnap = _logic.BuildInventorySnapshot(crateEntity);

        UpdateContractsProgress(comp, userSnap, crateSnap);

        var balancesByCurrency = new Dictionary<string, int>(comp.CurrencyWhitelist.Count, StringComparer.Ordinal);
        foreach (var cur in comp.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;

            balancesByCurrency[cur] = userSnap.StackTypeCounts.TryGetValue(cur, out var b) ? b : 0;
        }

        var remainingById = new Dictionary<string, int>(comp.Listings.Count);
        var ownedById = new Dictionary<string, int>(comp.Listings.Count);

        foreach (var l in comp.Listings)
        {
            if (string.IsNullOrWhiteSpace(l.Id))
                continue;
            remainingById[l.Id] = l.RemainingCount;
        }

        foreach (var l in comp.Listings)
        {
            if (string.IsNullOrWhiteSpace(l.Id) || string.IsNullOrWhiteSpace(l.ProductEntity))
                continue;
            ownedById[l.Id] = _logic.GetOwnedFromSnapshot(userSnap, l.ProductEntity, l.MatchMode);
        }

        var crateUnitsById = new Dictionary<string, int>();
        var crateTotals = new Dictionary<string, int>();

        if (crateUid is { } crate)
        {
            var plan = _logic.ComputeMassSellPlan(comp, crate);
            crateTotals = plan.IncomeByCurrency;
            crateUnitsById = new(plan.UnitsByListingId);
        }

        var contracts = comp.Contracts.Values.Select(c => MapContractToClient(c)).ToList();

        comp.UiRevision = unchecked(comp.UiRevision + 1);

        _ui.SetUiState(
            uid,
            StoreUiKey.Key,
            new StoreDynamicState(
                comp.UiRevision,
                comp.CatalogRevision,
                balancesByCurrency,
                remainingById,
                ownedById,
                crateUnitsById,
                crateTotals,
                contracts,
                comp.Listings.Any(l => l.Mode == StoreMode.Buy),
                comp.Listings.Any(l => l.Mode == StoreMode.Sell),
                comp.ContractPresets.Count > 0 || !string.IsNullOrWhiteSpace(comp.LegacyContractsPreset)
            ));
    }

    private bool TryFindWatchedRoot(EntityUid start, out EntityUid watchedRoot)
    {
        watchedRoot = default;
        if (_storesByWatchedRoot.Count == 0)
            return false;
        var cur = start;
        for (var i = 0; i < WatchedRootSearchLimit; i++)
        {
            if (_storesByWatchedRoot.TryGetValue(cur, out _))
            {
                watchedRoot = cur;
                return true;
            }

            if (!TryComp(cur, out TransformComponent? xform))
                return false;
            var parent = xform.ParentUid;
            if (parent == EntityUid.Invalid || parent == cur)
                return false;
            cur = parent;
        }

        return false;
    }

    private void RefreshStoresAffectedBy(EntityUid changedRoot)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;
        _logic.InvalidateInventoryCache(changedRoot);
        _pendingRefreshEntities.Add(changedRoot);
        if (_timing.CurTime < _nextCheck && _timing.CurTime >= _nextAccelAllowed)
        {
            _nextCheck = _timing.CurTime;
            _nextAccelAllowed = _timing.CurTime + TimeSpan.FromSeconds(MinAccelInterval);
        }

        if (_pendingRefreshEntities.Count > 4096)
        {
            foreach (var s in _openStoreUids)
                MarkDirty(s);
            _pendingRefreshEntities.Clear();
        }
    }

    private void OnUserEntInserted(EntityUid uid, ContainerManagerComponent comp, EntInsertedIntoContainerMessage args)
    {
        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnUserEntRemoved(EntityUid uid, ContainerManagerComponent comp, EntRemovedFromContainerMessage args)
    {
        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnStackCountChanged(EntityUid uid, StackComponent comp, ref StackCountChangedEvent args)
    {
        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void ProcessPendingRefreshes()
    {
        if (_pendingRefreshEntities.Count == 0 || _storesByWatchedRoot.Count == 0)
            return;
        _affectedStoresScratch.Clear();
        foreach (var root in _pendingRefreshEntities)
        {
            if (!Exists(root))
                continue;
            if (_storesByWatchedRoot.TryGetValue(root, out var stores))
            {
                foreach (var s in stores)
                    _affectedStoresScratch.Add(s);
            }
        }

        _pendingRefreshEntities.Clear();
        foreach (var s in _affectedStoresScratch)
            MarkDirty(s);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(CheckInterval);
        ProcessPendingRefreshes();
        if (_openStoreUids.Count == 0)
            return;
        _openStoresScratch.Clear();
        foreach (var u in _openStoreUids)
            _openStoresScratch.Add(u);
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

            if (!TryComp(userUid, out TransformComponent? userXform) || !_xform.InRange(
                xform.Coordinates,
                userXform.Coordinates,
                AutoCloseDistance))
            {
                CloseAndCleanUp(uid, userUid);
                store.CurrentUser = null;
                continue;
            }

            if (!_storeSystem.CanUseStore(uid, store, userUid))
            {
                CloseAndCleanUp(uid, userUid);
                store.CurrentUser = null;
                _popups.PopupEntity(Loc.GetString("ncstore-no-access"), uid, userUid);
                continue;
            }

            if (EnsureCrateWatchUpToDate(uid, userUid))
                MarkDirty(uid);
            if (_dirtyStores.Remove(uid))
                UpdateDynamicState(uid, store, userUid);
        }
    }

    private void CloseAndCleanUp(EntityUid storeUid, EntityUid? user = null)
    {
        if (user != null)
            _ui.CloseUi(storeUid, StoreUiKey.Key, user.Value);
        _openStoreUids.Remove(storeUid);
        UnregisterStoreWatch(storeUid);
        _dirtyStores.Remove(storeUid);
    }

    private bool EnsureCrateWatchUpToDate(EntityUid storeUid, EntityUid user)
    {
        EntityUid? crateUid = null;
        if (TryGetPulledClosedCrate(user, out var pulledCrate))
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

    private bool TryGetPulledClosedCrate(EntityUid user, out EntityUid crate)
    {
        crate = default;
        if (!TryComp(user, out PullerComponent? puller) || puller.Pulling is not { } pulled)
            return false;
        if (!TryComp(pulled, out EntityStorageComponent? storage) || storage.Open)
            return false;
        crate = pulled;
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
        if (crate is { } c)
            AddWatchedRoot(c, storeUid);
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
        SendCatalog(uid, comp, user);
        UpdateDynamicState(uid, comp, user);
    }

    private void SendCatalog(EntityUid store, NcStoreComponent comp, EntityUid user)
    {
        if (!_ui.IsUiOpen(store, StoreUiKey.Key, user))
            return;

        if (_catalogCache.TryGetValue(store, out var cachedList))
        {
            var hasBuy = cachedList.Any(l => l.Mode == StoreMode.Buy);
            var hasSell = cachedList.Any(l => l.Mode == StoreMode.Sell);

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

            var cat = l.Categories.Count > 0 ? l.Categories[0] : "Разное";

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

        _catalogCache[store] = list;

        {
            var hasBuy = list.Any(l => l.Mode == StoreMode.Buy);
            var hasSell = list.Any(l => l.Mode == StoreMode.Sell);

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
        _logic.InvalidateInventoryCache(user);
        if (TryGetPulledClosedCrate(user, out var crate))
            _logic.InvalidateInventoryCache(crate);
        var userSnap = _logic.BuildInventorySnapshot(user);
        NcStoreLogicSystem.InventorySnapshot? crateSnap = null;
        if (crate != default)
            crateSnap = _logic.BuildInventorySnapshot(crate);
        UpdateContractsProgress(comp, userSnap, crateSnap);
        if (!_contracts.TryClaim(uid, user, msg.ContractId))
            return;
        _popups.PopupEntity("Контракт выполнен!", uid, user);
        UpdateDynamicState(uid, comp, user);
    }

    private void MarkDirty(EntityUid storeUid)
    {
        if (storeUid != EntityUid.Invalid)
            _dirtyStores.Add(storeUid);
    }

    private bool TryPickUiCurrencyAndPrice(
        NcStoreComponent comp,
        StoreListingPrototype listing,
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
        if (c.Targets is { Count: > 0, })
        {
            foreach (var t in c.Targets)
                if (!string.IsNullOrWhiteSpace(t.TargetItem) && t.Required > 0)
                    targets.Add(new(t.TargetItem, t.Required, t.Progress) { MatchMode = t.MatchMode, });
        }
        else if (!string.IsNullOrWhiteSpace(c.TargetItem) && c.Required > 0)
            targets.Add(new(c.TargetItem, c.Required, c.Progress) { MatchMode = c.MatchMode, });

        var client = new ContractClientData(
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
            c.Description,
            targets,
            c.Repeatable);
        if (c.RewardCurrencies is { Count: > 0, })
            client.RewardCurrencies = new(c.RewardCurrencies);
        if (c.RewardItems is { Count: > 0, })
            client.RewardItems = new(c.RewardItems);
        return client;
    }

    private void UpdateContractsProgress(
        NcStoreComponent comp,
        in NcStoreLogicSystem.InventorySnapshot userSnap,
        NcStoreLogicSystem.InventorySnapshot? crateSnap
    )
    {
        if (comp.Contracts.Count == 0)
            return;
        foreach (var (_, contract) in comp.Contracts)
        {
            var targets = contract.Targets;
            if (targets.Count > 0)
            {
                var totalRequired = 0;
                var totalProgress = 0;
                foreach (var t in targets)
                {
                    if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
                    {
                        t.Progress = 0;
                        continue;
                    }

                    var owned = _logic.GetOwnedFromSnapshot(userSnap, t.TargetItem, t.MatchMode);
                    if (crateSnap.HasValue)
                        owned += _logic.GetOwnedFromSnapshot(crateSnap.Value, t.TargetItem, t.MatchMode);
                    var prog = Math.Min(owned, t.Required);
                    t.Progress = prog;
                    totalRequired += t.Required;
                    totalProgress += prog;
                }

                contract.Required = totalRequired;
                contract.Progress = totalProgress;
                if (targets.Count > 0)
                    contract.TargetItem = targets[0].TargetItem;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(contract.TargetItem) || contract.Required <= 0)
                {
                    contract.Progress = 0;
                    continue;
                }

                var owned = _logic.GetOwnedFromSnapshot(userSnap, contract.TargetItem, contract.MatchMode);
                if (crateSnap.HasValue)
                    owned += _logic.GetOwnedFromSnapshot(crateSnap.Value, contract.TargetItem, contract.MatchMode);
                contract.Progress = Math.Min(owned, contract.Required);
            }
        }
    }
}
