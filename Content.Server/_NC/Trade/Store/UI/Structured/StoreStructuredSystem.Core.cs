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
    private const int MaxVisibleListingIds = 256;
    private const int MaxVisibleListingIdLength = 96;
    private const int WatchedRootSearchLimit = 32;
    private const float CheckInterval = 1.0f;
    private readonly HashSet<EntityUid> _affectedStoresScratch = new();
    [Dependency] private readonly AudioSystem _audio = default!;
    private readonly Dictionary<EntityUid, (int Revision, List<StoreListingStaticData> List)> _catalogCache = new();
    [Dependency] private readonly NcContractSystem _contracts = default!;
    private readonly List<EntityUid> _deepCrateItemsScratch = new();
    private readonly List<EntityUid> _deepUserItemsScratch = new();
    private readonly HashSet<EntityUid> _dirtyStores = new();
    private readonly List<EntityUid> _dirtyStoresScratch = new();
    private readonly Dictionary<EntityUid, DynamicScratch> _dynamicScratchByStore = new();
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
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
    private const int MaxDynamicUpdatesPerTick = 8;

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
        SubscribeLocalEvent<NcStoreComponent, StoreSetVisibleListingsBoundUiMessage>(OnSetVisibleListings);
        SubscribeLocalEvent<AccessReaderComponent, AccessReaderConfigurationChangedEvent>(OnAccessReaderChanged);
        SubscribeLocalEvent<NcStoreComponent, ComponentShutdown>(OnStoreShutdown);
        SubscribeLocalEvent<ContainerManagerComponent, EntInsertedIntoContainerMessage>(OnUserEntInserted);
        SubscribeLocalEvent<ContainerManagerComponent, EntRemovedFromContainerMessage>(OnUserEntRemoved);
        SubscribeLocalEvent<StackComponent, StackCountChangedEvent>(OnStackCountChanged);
        SubscribeLocalEvent<EntParentChangedMessage>(OnWatchedEntityParentChanged);
        SubscribeLocalEvent<NcStoreComponent, ClaimContractBoundMessage>(OnClaimContract);
        SubscribeLocalEvent<NcStoreComponent, TakeContractBoundMessage>(OnTakeContract);
        SubscribeLocalEvent<NcStoreComponent, SkipContractBoundMessage>(OnSkipContract);
        SubscribeLocalEvent<NcStoreComponent, RequestContractPinpointerBoundMessage>(OnRequestContractPinpointer);
        SubscribeLocalEvent<EntityStorageComponent, StorageAfterOpenEvent>(OnStorageOpen);
        SubscribeLocalEvent<EntityStorageComponent, StorageAfterCloseEvent>(OnStorageClose);
    }


    private bool TryGetLockedUiUser(EntityUid store, NcStoreComponent comp, out EntityUid user)
    {
        user = default;
        if (comp.CurrentUser is not { } cur || cur == EntityUid.Invalid)
            return false;

        if (!_ui.IsUiOpen(store, StoreUiKey.Key, cur))
            return false;

        user = cur;
        return true;
    }


    private void OnSetVisibleListings(EntityUid uid, NcStoreComponent comp, StoreSetVisibleListingsBoundUiMessage msg)
    {
        if (!TryGetLockedUiUser(uid, comp, out var user))
            return;

        var ids = msg.Ids;
        if (ids.Length > MaxVisibleListingIds)
            ids = ids.Take(MaxVisibleListingIds).ToArray();

        if (ids.Length > 0)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (id.Length is 0 or > MaxVisibleListingIdLength)
                    ids[i] = string.Empty;
            }
        }

        var scratch = GetDynamicScratch(uid);
        if (!scratch.UpdateVisibleIds(ids))
            return;
        RequestDynamicRefresh(uid, comp, user);
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
        _contracts.ClearStoreRuntimeCaches(uid);

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
        RequestDynamicRefresh(uid, comp, user);
    }

    public void RequestDynamicRefresh(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        MarkDirty(uid);

        var now = _timing.CurTime;
        var scratch = GetDynamicScratch(uid);
        if (now < scratch.NextDynamicAllowed)
            return;

        _dirtyStores.Remove(uid);
        UpdateDynamicState(uid, comp, user);
        SetNextDynamicUpdateTime(scratch, now);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ProcessPendingRefreshes();
        ProcessRealtimeOpenStoreUpdates();
        ProcessDirtyStoreUpdates();
        ProcessOpenStoreValidityChecks();
    }

    private void ProcessRealtimeOpenStoreUpdates()
    {
        if (_openStoreUids.Count == 0)
            return;

        var now = _timing.CurTime;
        _openStoresScratch.Clear();
        _openStoresScratch.AddRange(_openStoreUids);

        foreach (var uid in _openStoresScratch)
            ProcessRealtimeOpenStoreUpdate(uid, now);
    }

    private void ProcessRealtimeOpenStoreUpdate(EntityUid uid, TimeSpan now)
    {
        if (!TryGetOpenStoreUser(uid, out var store, out var user))
            return;

        if (EnsureCrateWatchUpToDate(uid, user))
            MarkDirty(uid);

        if (!_contracts.HasRealtimeContractState(store) || !TryGetDynamicScratchForUpdate(uid, now, out var scratch))
            return;

        _dirtyStores.Remove(uid);
        UpdateDynamicState(uid, store, user);
        SetNextDynamicUpdateTime(scratch, now);
    }

    private void ProcessDirtyStoreUpdates()
    {
        if (_dirtyStores.Count == 0)
            return;

        var now = _timing.CurTime;
        var processed = 0;

        _dirtyStoresScratch.Clear();
        _dirtyStoresScratch.AddRange(_dirtyStores);

        foreach (var uid in _dirtyStoresScratch)
        {
            if (processed >= MaxDynamicUpdatesPerTick)
                break;

            if (!TryGetOpenStoreUser(uid, out var store, out var user))
            {
                _dirtyStores.Remove(uid);
                continue;
            }

            if (!TryGetDynamicScratchForUpdate(uid, now, out var scratch))
                continue;

            UpdateDynamicState(uid, store, user);
            SetNextDynamicUpdateTime(scratch, now);
            _dirtyStores.Remove(uid);
            processed++;
        }
    }

    private void ProcessOpenStoreValidityChecks()
    {
        if (!ShouldRunOpenStoreValidityCheck())
            return;

        _openStoresScratch.Clear();
        _openStoresScratch.AddRange(_openStoreUids);

        foreach (var uid in _openStoresScratch)
            ValidateOpenStore(uid);
    }

    private bool ShouldRunOpenStoreValidityCheck()
    {
        if (_timing.CurTime < _nextCheck)
            return false;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(CheckInterval);
        return _openStoreUids.Count > 0;
    }

    private bool TryGetOpenStoreUser(EntityUid uid, out NcStoreComponent store, out EntityUid user)
    {
        store = default!;
        user = default;

        if (!TryComp(uid, out NcStoreComponent? foundStore) || foundStore.CurrentUser is not { } currentUser)
            return false;

        store = foundStore;
        user = currentUser;
        return true;
    }

    private bool TryGetDynamicScratchForUpdate(EntityUid uid, TimeSpan now, out DynamicScratch scratch)
    {
        scratch = GetDynamicScratch(uid);
        return now >= scratch.NextDynamicAllowed;
    }

    private void SetNextDynamicUpdateTime(DynamicScratch scratch, TimeSpan now)
    {
        scratch.NextDynamicAllowed = now + TimeSpan.FromSeconds(MinDynamicInterval);
    }

    private void ValidateOpenStore(EntityUid uid)
    {
        if (!TryComp(uid, out NcStoreComponent? store) || !TryComp(uid, out TransformComponent? xform))
        {
            CloseAndCleanUp(uid);
            return;
        }

        if (store.CurrentUser is not { } userUid)
        {
            CloseAndCleanUp(uid);
            return;
        }

        if (!IsStoreUserInRange(xform, userUid))
        {
            CloseStoreForDetachedUser(uid, store, userUid);
            return;
        }

        if (_storeSystem.CanUseStore(uid, store, userUid))
            return;

        CloseStoreForNoAccess(uid, store, userUid);
    }

    private bool IsStoreUserInRange(TransformComponent storeXform, EntityUid userUid)
    {
        return TryComp(userUid, out TransformComponent? userXform) &&
               _xform.InRange(storeXform.Coordinates, userXform.Coordinates, AutoCloseDistance);
    }

    private void CloseStoreForDetachedUser(EntityUid uid, NcStoreComponent store, EntityUid userUid)
    {
        CloseAndCleanUp(uid, userUid);
        store.CurrentUser = null;
    }

    private void CloseStoreForNoAccess(EntityUid uid, NcStoreComponent store, EntityUid userUid)
    {
        CloseAndCleanUp(uid, userUid);
        store.CurrentUser = null;
        _popups.PopupEntity(Loc.GetString("nc-store-no-access"), uid, userUid);
    }

    private void CloseAndCleanUp(EntityUid storeUid, EntityUid? user = null)
    {
        if (_watchByStore.TryGetValue(storeUid, out var info))
        {
            if (info.User != EntityUid.Invalid)
                _inventory.InvalidateInventoryCache(info.User);

            if (info.Crate is { } crate)
                _inventory.InvalidateInventoryCache(crate);
        }

        if (user != null)
            _ui.CloseUi(storeUid, StoreUiKey.Key, user.Value);

        if (_dynamicScratchByStore.TryGetValue(storeUid, out var scratch))
            scratch.UpdateVisibleIds(null);
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
                    _inventory.InvalidateInventoryCache(oldCrate);
                if (crateUid is { } newCrate)
                    _inventory.InvalidateInventoryCache(newCrate);
            }

            if (prev.User != user)
            {
                if (prev.User != EntityUid.Invalid)
                    _inventory.InvalidateInventoryCache(prev.User);
                _inventory.InvalidateInventoryCache(user);
            }
        }
        else
        {
            _inventory.InvalidateInventoryCache(user);
            if (crateUid is { } newCrate)
                _inventory.InvalidateInventoryCache(newCrate);
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
        _inventory.InvalidateInventoryCache(user);
        if (crate is { } c)
        {
            AddWatchedRoot(c, storeUid);
            _inventory.InvalidateInventoryCache(c);
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
        RequestDynamicRefresh(uid, comp, user);
    }


    private void SendCatalog(EntityUid store, NcStoreComponent comp, EntityUid user)
    {
        if (!_ui.IsUiOpen(store, StoreUiKey.Key, user))
            return;

        var catalog = GetOrBuildCatalog(store, comp);
        var msg = BuildCatalogMessage(comp, catalog);
        _ui.ServerSendUiMessage((store, null), StoreUiKey.Key, msg, user);
    }

    private List<StoreListingStaticData> GetOrBuildCatalog(EntityUid store, NcStoreComponent comp)
    {
        if (_catalogCache.TryGetValue(store, out var cached) && cached.Revision == comp.CatalogRevision)
            return cached.List;

        var list = BuildCatalogEntries(comp);
        _catalogCache[store] = (comp.CatalogRevision, list);
        return list;
    }

    private List<StoreListingStaticData> BuildCatalogEntries(NcStoreComponent comp)
    {
        var list = new List<StoreListingStaticData>(comp.Listings.Count);

        foreach (var listing in comp.Listings)
        {
            if (!TryBuildCatalogEntry(comp, listing, out var entry))
                continue;

            list.Add(entry);
        }

        return list;
    }

    private bool TryBuildCatalogEntry(
        NcStoreComponent comp,
        NcStoreListingDef listing,
        out StoreListingStaticData entry)
    {
        entry = null!;

        if (string.IsNullOrWhiteSpace(listing.Id) || string.IsNullOrWhiteSpace(listing.ProductEntity))
            return false;

        if (!TryPickUiCurrencyAndPrice(comp, listing, out var currencyId, out var price))
            return false;

        var category = listing.Categories.Count > 0
            ? listing.Categories[0]
            : Loc.GetString("nc-store-category-fallback");

        entry = new(
            listing.Id,
            listing.Mode,
            category,
            listing.ProductEntity,
            price,
            currencyId,
            listing.UnitsPerPurchase
        );

        return true;
    }

    private StoreCatalogMessage BuildCatalogMessage(
        NcStoreComponent comp,
        List<StoreListingStaticData> list)
    {
        var (hasBuy, hasSell) = GetCatalogModeFlags(list);

        return new(
            comp.CatalogRevision,
            list,
            hasBuy,
            hasSell,
            comp.ContractPresets.Count > 0
        );
    }

    private static (bool HasBuy, bool HasSell) GetCatalogModeFlags(List<StoreListingStaticData> list)
    {
        var hasBuy = false;
        var hasSell = false;

        foreach (var listing in list)
        {
            if (listing.Mode == StoreMode.Buy)
                hasBuy = true;
            else if (listing.Mode == StoreMode.Sell)
                hasSell = true;

            if (hasBuy && hasSell)
                break;
        }

        return (hasBuy, hasSell);
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
        if (!TryGetLockedUiUser(uid, comp, out var user))
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

        if (TryComp(uid, out TransformComponent? sX) && TryComp(user, out TransformComponent? uX) &&
            !_xform.InRange(sX.Coordinates, uX.Coordinates, AutoCloseDistance))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            CloseAndCleanUp(uid);
            return;
        }

        EnsureCrateWatchUpToDate(uid, user);
        RequestDynamicRefresh(uid, comp, user);
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
            if (listing.Cost.TryGetValue(cur, out var p) && p > 0)
            {
                currencyId = cur;
                price = p;
                return true;
            }
        }

        KeyValuePair<string, int>? best = null;
        foreach (var kv in listing.Cost)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0)
                continue;

            if (best == null || string.CompareOrdinal(kv.Key, best.Value.Key) < 0)
                best = kv;
        }

        if (best == null)
            return false;

        currencyId = best.Value.Key;
        price = best.Value.Value;
        return true;
    }

}

