using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Containers;


namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    private readonly record struct DynamicTabState(bool HasBuyTab, bool HasSellTab, bool HasContractsTab);

    private readonly record struct DynamicContractNeeds(
        bool HasTakenContracts,
        bool NeedUserItems,
        bool NeedCrateItems,
        bool NeedStoreWorldItems);

    private readonly record struct DynamicScanNeeds(
        bool NeedUserSnapshot,
        bool NeedUserItems,
        bool NeedCrateScan);

    public void UpdateDynamicState(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            return;

        if (!_storesUpdatingDynamic.Add(uid))
        {
            Logger.GetSawmill("ncstore-structured").Warning(
                $"[StoreStructured] Re-entrant UpdateDynamicState on {ToPrettyString(uid)} skipped.");
            return;
        }

        try
        {
            var scratch = GetDynamicScratch(uid);
            var crateUid = GetDynamicCrate(user);
            UpdateStoreWatch(uid, user, crateUid);
            var tabs = GetDynamicTabState(comp);
            var contractNeeds = GetDynamicContractNeeds(comp, tabs.HasContractsTab);
            var scanNeeds = GetDynamicScanNeeds(comp, crateUid, tabs.HasSellTab, contractNeeds);
            var userSnap = ScanDynamicUserInventory(user, scanNeeds, scratch);
            ScanDynamicCrateInventory(crateUid, scanNeeds, scratch);
            UpdateDynamicContractProgress(uid, comp, user, crateUid, tabs, contractNeeds, scratch);

            var buf = scratch.GetWriteBuffer();
            buf.Clear();

            PopulateDynamicBalances(comp, userSnap, buf);
            PopulateDynamicListings(comp, userSnap, scratch, buf);
            PopulateDynamicCratePreview(comp, crateUid, tabs.HasSellTab, scanNeeds.NeedCrateScan, scratch, buf);
            PopulateDynamicContracts(comp, tabs.HasContractsTab, scratch, buf);
            PopulateDynamicContractSkip(uid, comp, tabs.HasContractsTab, buf);
            PushDynamicState(uid, comp, tabs, scratch, buf);
        }
        finally
        {
            _storesUpdatingDynamic.Remove(uid);
        }
    }

    private EntityUid? GetDynamicCrate(EntityUid user)
    {
        return _logic.TryGetPulledClosedCrate(user, out var pulledCrate)
            ? pulledCrate
            : null;
    }

    private DynamicTabState GetDynamicTabState(NcStoreComponent comp)
    {
        var hasBuyTab = false;
        var hasSellTab = false;

        foreach (var listing in comp.Listings)
        {
            if (listing.Mode == StoreMode.Buy)
                hasBuyTab = true;
            else if (listing.Mode == StoreMode.Sell)
                hasSellTab = true;

            if (hasBuyTab && hasSellTab)
                break;
        }

        return new(hasBuyTab, hasSellTab, HasContractsProfile(comp));
    }

    private DynamicContractNeeds GetDynamicContractNeeds(NcStoreComponent comp, bool hasContractsTab)
    {
        if (!hasContractsTab)
            return default;

        _contracts.AnalyzeContractProgressRequirements(
            comp,
            out var hasTakenContracts,
            out var needUserItems,
            out var needCrateItems,
            out var needStoreWorldItems);

        return new(hasTakenContracts, needUserItems, needCrateItems, needStoreWorldItems);
    }

    private static DynamicScanNeeds GetDynamicScanNeeds(
        NcStoreComponent comp,
        EntityUid? crateUid,
        bool hasSellTab,
        DynamicContractNeeds contractNeeds)
    {
        var needUserSnapshot = NeedsDynamicUserSnapshot(comp);
        var needUserItems = needUserSnapshot || contractNeeds.NeedUserItems;
        var needCrateScan = crateUid != null && (hasSellTab || contractNeeds.NeedCrateItems);
        return new(needUserSnapshot, needUserItems, needCrateScan);
    }

    private static bool NeedsDynamicUserSnapshot(NcStoreComponent comp)
    {
        if (comp.CurrencyWhitelist.Count > 0)
            return true;

        foreach (var listing in comp.Listings)
        {
            if (!string.IsNullOrWhiteSpace(listing.ProductEntity))
                return true;
        }

        return false;
    }

    private NcInventorySnapshot? ScanDynamicUserInventory(
        EntityUid user,
        DynamicScanNeeds scanNeeds,
        DynamicScratch scratch)
    {
        if (scanNeeds.NeedUserSnapshot)
        {
            _inventory.ScanInventory(user, scratch.DeepUserItems, scratch.UserSnapshot);
            return scratch.UserSnapshot;
        }

        if (scanNeeds.NeedUserItems)
        {
            _inventory.ScanInventoryItems(user, scratch.DeepUserItems);
            scratch.UserSnapshot.Clear();
            return null;
        }

        scratch.DeepUserItems.Clear();
        scratch.UserSnapshot.Clear();
        return null;
    }

    private void ScanDynamicCrateInventory(
        EntityUid? crateUid,
        DynamicScanNeeds scanNeeds,
        DynamicScratch scratch)
    {
        if (scanNeeds.NeedCrateScan && crateUid is { } crateEntity)
        {
            _inventory.ScanInventoryItems(crateEntity, scratch.DeepCrateItems);
            return;
        }

        scratch.DeepCrateItems.Clear();
    }

    private void UpdateDynamicContractProgress(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid user,
        EntityUid? crateUid,
        DynamicTabState tabs,
        DynamicContractNeeds contractNeeds,
        DynamicScratch scratch)
    {
        if (!tabs.HasContractsTab || !contractNeeds.HasTakenContracts)
            return;

        _contracts.UpdateContractsProgress(
            store,
            comp,
            user,
            scratch.DeepUserItems,
            crateUid,
            crateUid != null ? scratch.DeepCrateItems : null,
            contractNeeds.NeedStoreWorldItems);
    }

    private static void PopulateDynamicBalances(
        NcStoreComponent comp,
        NcInventorySnapshot? userSnap,
        DynamicStateBuffer buf)
    {
        if (userSnap == null)
            return;

        foreach (var currency in comp.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(currency))
                continue;

            buf.BalancesByCurrency[currency] = userSnap.StackTypeCounts.TryGetValue(currency, out var balance)
                ? balance
                : 0;
        }
    }

    private void PopulateDynamicListings(
        NcStoreComponent comp,
        NcInventorySnapshot? userSnap,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        foreach (var listing in comp.Listings)
        {
            if (string.IsNullOrWhiteSpace(listing.Id))
                continue;

            var isVisibleBuyListing = IsVisibleBuyListing(listing, scratch);
            if (listing.Mode == StoreMode.Buy && !isVisibleBuyListing)
                continue;

            if (ShouldSendListingRemaining(listing, isVisibleBuyListing))
                buf.RemainingById[listing.Id] = listing.RemainingCount;

            if (userSnap == null || string.IsNullOrWhiteSpace(listing.ProductEntity))
                continue;

            var owned = _inventory.GetOwnedFromSnapshot(userSnap, listing.ProductEntity, listing.MatchMode);
            if (ShouldSendListingOwned(owned, isVisibleBuyListing))
                buf.OwnedById[listing.Id] = owned;
        }
    }

    private static bool IsVisibleBuyListing(NcStoreListingDef listing, DynamicScratch scratch)
    {
        return listing.Mode == StoreMode.Buy && scratch.ShouldSendBuyDynamicFor(listing.Id);
    }

    private static bool ShouldSendListingRemaining(NcStoreListingDef listing, bool isVisibleBuyListing)
    {
        return listing.RemainingCount != -1 || isVisibleBuyListing;
    }

    private static bool ShouldSendListingOwned(int owned, bool isVisibleBuyListing)
    {
        return owned > 0 || isVisibleBuyListing;
    }

    private void PopulateDynamicCratePreview(
        NcStoreComponent comp,
        EntityUid? crateUid,
        bool hasSellTab,
        bool needCrateScan,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        if (!hasSellTab || !needCrateScan || crateUid is not { } crate)
        {
            scratch.ResetCachedCratePreview();
            return;
        }

        var inventoryRevision = _logic.GetInventoryRevision(crate);
        if (scratch.TryPopulateCachedCratePreview(crate, comp.CatalogRevision, inventoryRevision, buf))
            return;

        var plan = _logic.ComputeMassSellPlanFromCachedItems(comp, crate, scratch.DeepCrateItems);
        scratch.CacheCratePreview(crate, comp.CatalogRevision, inventoryRevision, plan);
        scratch.TryPopulateCachedCratePreview(crate, comp.CatalogRevision, inventoryRevision, buf);
    }

    private void PopulateDynamicContracts(
        NcStoreComponent comp,
        bool hasContractsTab,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        if (!hasContractsTab || comp.Contracts.Count == 0)
        {
            scratch.ResetContractsFingerprint();
            return;
        }

        foreach (var contract in comp.Contracts.Values)
            buf.Contracts.Add(MapContractToClient(contract));

        buf.Contracts.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));

        var contractsFingerprint = buf.Contracts.ComputeFingerprint();
        if (!scratch.ShouldRebuildContracts(contractsFingerprint))
        {
            buf.Contracts.Clear();
            buf.Contracts.AddRange(scratch.GetReadBuffer().Contracts);
        }
    }

    private void PopulateDynamicContractSkip(
        EntityUid store,
        NcStoreComponent comp,
        bool hasContractsTab,
        DynamicStateBuffer buf)
    {
        if (!hasContractsTab || !_contracts.TryGetContractSkipInfo(store, comp, out var skipCurrency, out var skipCost))
            return;

        buf.ContractSkipCost = skipCost;
        buf.ContractSkipCurrency = skipCurrency;
    }

    private void PushDynamicState(
        EntityUid store,
        NcStoreComponent comp,
        DynamicTabState tabs,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        if (scratch.EqualsLast(buf, comp.CatalogRevision, tabs.HasBuyTab, tabs.HasSellTab, tabs.HasContractsTab))
            return;

        comp.UiRevision = unchecked(comp.UiRevision + 1);

        _ui.SetUiState(
            store,
            StoreUiKey.Key,
            new StoreDynamicState(
                comp.UiRevision,
                comp.CatalogRevision,
                new Dictionary<string, int>(buf.BalancesByCurrency),
                new Dictionary<string, int>(buf.RemainingById),
                new Dictionary<string, int>(buf.OwnedById),
                new Dictionary<string, int>(buf.CrateUnitsById),
                new Dictionary<string, int>(buf.CrateTotals),
                new List<ContractClientData>(buf.Contracts),
                tabs.HasBuyTab,
                tabs.HasSellTab,
                tabs.HasContractsTab,
                buf.ContractSkipCost,
                buf.ContractSkipCurrency
            )
        );

        scratch.Commit(comp.CatalogRevision, tabs.HasBuyTab, tabs.HasSellTab, tabs.HasContractsTab);
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

        if (_pendingRefreshEntities.Add(changedRoot))
            _inventory.InvalidateInventoryCache(changedRoot);

        if (_timing.CurTime < _nextOpenStoreValidityCheck && _timing.CurTime >= _nextAccelAllowed)
        {
            _nextOpenStoreValidityCheck = _timing.CurTime;
            _nextAccelAllowed = _timing.CurTime + TimeSpan.FromSeconds(MinAccelInterval);
        }

        if (_pendingRefreshEntities.Count > 4096)
        {
            foreach (var s in _openStoreUids)
            {
                if (_watchByStore.TryGetValue(s, out var watch))
                {
                    if (watch.User != EntityUid.Invalid)
                        _inventory.InvalidateInventoryCache(watch.User);
                    if (watch.Crate is { } crate)
                        _inventory.InvalidateInventoryCache(crate);
                }

                MarkDirty(s);
            }

            _pendingRefreshEntities.Clear();
        }
    }

    private void OnUserEntInserted(EntityUid uid, ContainerManagerComponent comp, EntInsertedIntoContainerMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (!IsNearAnyOpenStore(uid))
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnUserEntRemoved(EntityUid uid, ContainerManagerComponent comp, EntRemovedFromContainerMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (!IsNearAnyOpenStore(uid))
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnStackCountChanged(EntityUid uid, StackComponent comp, ref StackCountChangedEvent args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (!IsNearAnyOpenStore(uid))
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnWatchedEntityParentChanged(ref EntParentChangedMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        var nearEntity = IsNearAnyOpenStore(args.Entity);
        var nearOldParent = args.OldParent is { } oldParentCandidate &&
                            oldParentCandidate != EntityUid.Invalid &&
                            IsNearAnyOpenStore(oldParentCandidate);

        if (!nearEntity && !nearOldParent)
            return;

        EntityUid? refreshedRoot = null;

        if (nearEntity && TryFindWatchedRoot(args.Entity, out var currentRoot))
        {
            RefreshStoresAffectedBy(currentRoot);
            refreshedRoot = currentRoot;
        }

        if (args.OldParent is not { } oldParent || oldParent == EntityUid.Invalid)
            return;

        if (!nearOldParent)
            return;

        if (!TryFindWatchedRoot(oldParent, out var previousRoot))
            return;

        if (refreshedRoot == previousRoot)
            return;

        RefreshStoresAffectedBy(previousRoot);
    }


    private void ProcessPendingRefreshes()
    {
        if (_pendingRefreshEntities.Count == 0)
            return;

        if (_storesByWatchedRoot.Count == 0)
        {
            // No active watchers: drop stale pending roots to avoid carrying "air cache"
            // between unrelated store sessions.
            _pendingRefreshEntities.Clear();
            return;
        }
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

}





