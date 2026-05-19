using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;


namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    private const double SlowBarterAvailabilityMs = 5d;
    private const double SlowCratePreviewMs = 5d;
    private const double SlowDynamicStateMs = 10d;

    private readonly record struct DynamicTabState(bool HasBuyTab, bool HasSellTab, bool HasBarterTab, bool HasContractsTab);

    private readonly record struct DynamicContractNeeds(
        bool HasTakenContracts,
        bool NeedUserItems,
        bool NeedCrateItems,
        bool NeedStoreWorldItems);

    private readonly record struct DynamicScanNeeds(
        bool NeedUserSnapshot,
        bool NeedUserItems,
        bool NeedCrateScan);

    private readonly StoreDynamicStatePublisher _dynamicStatePublisher = new();

    public void UpdateDynamicState(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            return;

        var dynamicStarted = System.Diagnostics.Stopwatch.GetTimestamp();
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
            PopulateDynamicListings(comp, user, userSnap, scratch, buf);
            PopulateDynamicCratePreview(uid, comp, crateUid, tabs.HasSellTab, scanNeeds.NeedCrateScan, scratch, buf);
            PopulateDynamicContracts(uid, comp, tabs.HasContractsTab, scratch, buf);
            PopulateDynamicContractSkip(uid, comp, tabs.HasContractsTab, buf);
            PushDynamicState(uid, comp, tabs, scratch, buf);

            var elapsed = GetElapsedMilliseconds(dynamicStarted);
            if (elapsed > SlowDynamicStateMs)
            {
                Sawmill.Info(
                    $"[StoreStructured] UpdateDynamicState took {elapsed:F2} ms for {ToPrettyString(uid)} " +
                    $"(listings={comp.Listings.Count}, contracts={comp.Contracts.Count}).");
            }
        }
        finally
        {
            _storesUpdatingDynamic.Remove(uid);
        }
    }

    private static double GetElapsedMilliseconds(long started)
    {
        return (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d /
               System.Diagnostics.Stopwatch.Frequency;
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
        var hasBarterTab = false;

        foreach (var listing in comp.Listings)
        {
            if (listing.Mode == StoreMode.Buy)
                hasBuyTab = true;
            else if (listing.Mode == StoreMode.Sell)
                hasSellTab = true;
            else if (listing.Mode == StoreMode.Barter)
                hasBarterTab = true;

            if (hasBuyTab && hasSellTab && hasBarterTab)
                break;
        }

        return new(hasBuyTab, hasSellTab, hasBarterTab, HasContractsProfile(comp));
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

            if (listing.Mode == StoreMode.Barter && listing.BarterCost.Count > 0)
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
            // Keep progress preview consistent with claim planning: the pulled closed crate
            // itself may be the turn-in target.
            scratch.DeepCrateItems.Add(crateEntity);
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
        EntityUid user,
        NcInventorySnapshot? userSnap,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        var barterContextPrepared = false;
        var barterListings = 0;
        long barterTicks = 0;

        foreach (var listing in comp.Listings)
        {
            if (string.IsNullOrWhiteSpace(listing.Id))
                continue;

            var isVisibleBuyListing = IsVisibleBuyListing(listing, scratch);
            if (listing.Mode == StoreMode.Buy && !isVisibleBuyListing)
                continue;

            buf.ListingScopeIds.Add(listing.Id);

            if (ShouldSendListingRemaining(listing, isVisibleBuyListing))
                buf.RemainingById[listing.Id] = listing.RemainingCount;

            if (userSnap == null)
                continue;

            if (listing.Mode != StoreMode.Barter && string.IsNullOrWhiteSpace(listing.ProductEntity))
                continue;

            int owned;
            if (listing.Mode == StoreMode.Barter)
            {
                var barterStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!barterContextPrepared)
                {
                    _logic.PrepareBarterAvailabilityContext(user, scratch.DeepUserItems, scratch.BarterAvailability);
                    barterContextPrepared = true;
                }

                owned = _logic.GetMaxBarterCount(user, listing, userSnap, scratch.BarterAvailability);
                barterTicks += System.Diagnostics.Stopwatch.GetTimestamp() - barterStarted;
                barterListings++;
            }
            else
            {
                owned = _inventory.GetOwnedFromSnapshot(userSnap, listing.ProductEntity, listing.MatchMode);
            }

            if (ShouldSendListingOwned(owned, isVisibleBuyListing) || listing.Mode == StoreMode.Barter)
                buf.OwnedById[listing.Id] = owned;
        }

        if (barterListings > 0)
        {
            var barterMs = barterTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
            if (barterMs > SlowBarterAvailabilityMs)
            {
                Sawmill.Info(
                    $"[StoreStructured] Barter availability took {barterMs:F2} ms " +
                    $"for {barterListings} listings in profile '{comp.Profile}'.");
            }
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
        EntityUid store,
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

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var plan = _logic.ComputeMassSellPlanFromCachedItems(store, comp, crate, scratch.DeepCrateItems);
        var elapsed = GetElapsedMilliseconds(started);
        if (elapsed > SlowCratePreviewMs)
        {
            Sawmill.Info(
                $"[StoreStructured] Crate preview took {elapsed:F2} ms for {ToPrettyString(crate)} " +
                $"(items={scratch.DeepCrateItems.Count}, listings={comp.Listings.Count}).");
        }

        scratch.CacheCratePreview(crate, comp.CatalogRevision, inventoryRevision, plan);
        scratch.TryPopulateCachedCratePreview(crate, comp.CatalogRevision, inventoryRevision, buf);
    }

    private void PopulateDynamicContracts(
        EntityUid store,
        NcStoreComponent comp,
        bool hasContractsTab,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        if (!hasContractsTab || comp.Contracts.Count == 0)
            return;

        var signature = ComputeContractsSignature(store, comp);
        if (scratch.TryPopulateCachedContracts(signature, buf))
            return;

        foreach (var contract in comp.Contracts.Values)
            buf.Contracts.Add(MapContractToClient(store, contract));

        buf.Contracts.Sort(CompareContractsForUi);
        scratch.CacheContracts(signature, buf.Contracts);
    }

    private int ComputeContractsSignature(EntityUid store, NcStoreComponent comp)
    {
        unchecked
        {
            var contracts = new List<ContractServerData>(comp.Contracts.Values);
            contracts.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));

            var hash = 17;
            AddHash(ref hash, contracts.Count);
            for (var i = 0; i < contracts.Count; i++)
                AddHash(ref hash, ComputeContractSignature(store, contracts[i]));

            return hash;
        }
    }

    private int ComputeContractSignature(EntityUid store, ContractServerData contract)
    {
        unchecked
        {
            var hash = 17;
            AddHash(ref hash, contract.Id);
            AddHash(ref hash, contract.Name);
            AddHash(ref hash, contract.Description);
            AddHash(ref hash, contract.Repeatable);
            AddHash(ref hash, contract.Taken);
            AddHash(ref hash, SupportsContractPinpointer(contract));
            AddHash(ref hash, _contracts.CanPartiallyTurnInNow(store, contract.Id, contract));
            AddHash(ref hash, contract.ExecutionKind);
            AddHash(ref hash, contract.FlowStatus);
            AddHash(ref hash, contract.Completed);
            AddHash(ref hash, contract.TargetItem);
            AddHash(ref hash, contract.MatchMode);
            AddHash(ref hash, ResolveContractTurnInItem(contract));
            AddHash(ref hash, contract.Required);
            AddHash(ref hash, contract.Progress);
            AddHash(ref hash, contract.Config.RetrievalSourceHint);
            AddHash(ref hash, contract.Config.RetrievalDestinationHint);
            AddHash(ref hash, IsRetrievalRouteContract(contract));
            AddHash(ref hash, contract.Config.RetrievalClaimMode);
            AddHash(ref hash, IsRetrievalBearerProofContract(contract));
            AddHash(ref hash, contract.Config.HuntCompletionMode);
            AddHash(ref hash, contract.Config.GhostRoleCompletionMode);
            AddHash(ref hash, contract.OfferPoolId);
            AddHash(ref hash, contract.OfferPoolName);
            AddHash(ref hash, contract.OfferPoolOrder);
            AddHash(ref hash, contract.OfferPoolColor);
            AddRuntimeHash(ref hash, contract.Runtime);
            AddTargetsHash(ref hash, contract.Targets);
            AddRewardsHash(ref hash, contract.Rewards);
            return hash;
        }
    }

    private static void AddRuntimeHash(ref int hash, ContractRuntimeContextData? runtime)
    {
        if (runtime == null)
        {
            AddHash(ref hash, 0);
            return;
        }

        AddHash(ref hash, runtime.Stage);
        AddHash(ref hash, runtime.StageGoal);
        AddHash(ref hash, runtime.AcceptTimeoutRemainingSeconds);
        AddHash(ref hash, runtime.GhostRoleSurvivalRemainingSeconds);
        AddHash(ref hash, runtime.GhostRolePendingAcceptance);
        AddHash(ref hash, runtime.Failed);
        AddHash(ref hash, runtime.Outcome);
        AddHash(ref hash, runtime.FailureReason);
        AddHash(ref hash, runtime.StatusHint);
    }

    private static void AddTargetsHash(ref int hash, List<ContractTargetServerData>? targets)
    {
        if (targets == null)
        {
            AddHash(ref hash, 0);
            return;
        }

        AddHash(ref hash, targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            AddHash(ref hash, target.TargetItem);
            AddHash(ref hash, target.Required);
            AddHash(ref hash, target.Progress);
            AddHash(ref hash, target.MatchMode);
        }
    }

    private static void AddRewardsHash(ref int hash, List<ContractRewardData>? rewards)
    {
        if (rewards == null)
        {
            AddHash(ref hash, 0);
            return;
        }

        AddHash(ref hash, rewards.Count);
        for (var i = 0; i < rewards.Count; i++)
        {
            var reward = rewards[i];
            AddHash(ref hash, reward.Type);
            AddHash(ref hash, reward.Id);
            AddHash(ref hash, reward.Amount);
        }
    }

    private static void AddHash<T>(ref int hash, T value)
    {
        unchecked
        {
            hash = hash * 31 + EqualityComparer<T>.Default.GetHashCode(value!);
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

    private static int CompareContractsForUi(ContractClientData left, ContractClientData right)
    {
        var poolOrder = left.OfferPoolOrder.CompareTo(right.OfferPoolOrder);
        if (poolOrder != 0)
            return poolOrder;

        var name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        if (name != 0)
            return name;

        return string.CompareOrdinal(left.Id, right.Id);
    }

    private void PushDynamicState(
        EntityUid store,
        NcStoreComponent comp,
        DynamicTabState tabs,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        _dynamicStatePublisher.PublishIfChanged(_ui, store, comp, tabs, scratch, buf);
    }

    private sealed class StoreDynamicStatePublisher
    {
        public void PublishIfChanged(
            UserInterfaceSystem ui,
            EntityUid store,
            NcStoreComponent comp,
            DynamicTabState tabs,
            DynamicScratch scratch,
            DynamicStateBuffer buf)
        {
            if (scratch.EqualsLast(
                    buf,
                    comp.CatalogRevision,
                    tabs.HasBuyTab,
                    tabs.HasSellTab,
                    tabs.HasBarterTab,
                    tabs.HasContractsTab))
            {
                return;
            }

            comp.UiRevision = unchecked(comp.UiRevision + 1);

            ui.SetUiState(
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
                    tabs.HasBarterTab,
                    tabs.HasContractsTab,
                    buf.ContractSkipCost,
                    buf.ContractSkipCurrency,
                    scratch.HasVisibleIds,
                    new List<string>(buf.ListingScopeIds)
                )
            );

            scratch.Commit(comp.CatalogRevision, tabs.HasBuyTab, tabs.HasSellTab, tabs.HasBarterTab, tabs.HasContractsTab);
        }
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

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnUserEntRemoved(EntityUid uid, ContainerManagerComponent comp, EntRemovedFromContainerMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnStackCountChanged(EntityUid uid, StackComponent comp, ref StackCountChangedEvent args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        if (TryFindWatchedRoot(uid, out var r))
            RefreshStoresAffectedBy(r);
    }

    private void OnWatchedEntityParentChanged(ref EntParentChangedMessage args)
    {
        if (_storesByWatchedRoot.Count == 0)
            return;

        EntityUid? refreshedRoot = null;

        if (TryFindWatchedRoot(args.Entity, out var currentRoot))
        {
            RefreshStoresAffectedBy(currentRoot);
            refreshedRoot = currentRoot;
        }

        if (args.OldParent is not { } oldParent || oldParent == EntityUid.Invalid)
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





