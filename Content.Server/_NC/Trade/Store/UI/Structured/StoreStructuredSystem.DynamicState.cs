using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Containers;


namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    public void UpdateDynamicState(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        EntityUid? crateUid = null;
        if (_logic.TryGetPulledClosedCrate(user, out var pulledCrate))
            crateUid = pulledCrate;

        UpdateStoreWatch(uid, user, crateUid);
        _logic.ScanInventory(user, _deepUserItemsScratch, _userSnapScratch);
        var userSnap = _userSnapScratch;
        NcInventorySnapshot? crateSnap = null;
        if (crateUid is { } crateEntity)
        {
            _logic.ScanInventory(crateEntity, _deepCrateItemsScratch, _crateSnapScratch);
            crateSnap = _crateSnapScratch;
        }

        UpdateContractsProgress(comp, userSnap, crateSnap);

        var scratch = GetDynamicScratch(uid);
        var buf = scratch.GetWriteBuffer();
        buf.Clear();

        foreach (var cur in comp.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;

            buf.BalancesByCurrency[cur] = userSnap.StackTypeCounts.TryGetValue(cur, out var b) ? b : 0;
        }

        var hasBuyTab = false;
        var hasSellTab = false;

        foreach (var l in comp.Listings)
        {
            if (l.Mode == StoreMode.Buy)
                hasBuyTab = true;
            else if (l.Mode == StoreMode.Sell)
                hasSellTab = true;

            if (string.IsNullOrWhiteSpace(l.Id))
                continue;

            buf.RemainingById[l.Id] = l.RemainingCount;

            if (!string.IsNullOrWhiteSpace(l.ProductEntity))
            {
                // ИСПРАВЛЕНО: GetOwnedFromSnapshot теперь в _logic
                buf.OwnedById[l.Id] = _logic.GetOwnedFromSnapshot(userSnap, l.ProductEntity, l.MatchMode);
            }
        }

        if (crateUid is { } crate)
        {
            var plan = _logic.ComputeMassSellPlanFromCachedItems(comp, crate, _deepCrateItemsScratch);

            foreach (var kvp in plan.UnitsByListingId)
                if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > 0)
                    buf.CrateUnitsById[kvp.Key] = kvp.Value;

            foreach (var kvp in plan.IncomeByCurrency)
                if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > 0)
                    buf.CrateTotals[kvp.Key] = kvp.Value;
        }

        foreach (var c in comp.Contracts.Values)
            buf.Contracts.Add(MapContractToClient(c));

        comp.UiRevision = unchecked(comp.UiRevision + 1);

        _ui.SetUiState(
            uid,
            StoreUiKey.Key,
            new StoreDynamicState(
                comp.UiRevision,
                comp.CatalogRevision,
                buf.BalancesByCurrency,
                buf.RemainingById,
                buf.OwnedById,
                buf.CrateUnitsById,
                buf.CrateTotals,
                buf.Contracts,
                hasBuyTab,
                hasSellTab,
                comp.ContractPresets.Count > 0 || !string.IsNullOrWhiteSpace(comp.LegacyContractsPreset)
            ));

        scratch.Commit();
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
            _logic.InvalidateInventoryCache(changedRoot);

        if (_timing.CurTime < _nextCheck && _timing.CurTime >= _nextAccelAllowed)
        {
            _nextCheck = _timing.CurTime;
            _nextAccelAllowed = _timing.CurTime + TimeSpan.FromSeconds(MinAccelInterval);
        }

        if (_pendingRefreshEntities.Count > 4096)
        {
            foreach (var s in _openStoreUids)
            {
                if (_watchByStore.TryGetValue(s, out var watch))
                {
                    if (watch.User != EntityUid.Invalid)
                        _logic.InvalidateInventoryCache(watch.User);
                    if (watch.Crate is { } crate)
                        _logic.InvalidateInventoryCache(crate);
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

    private void UpdateContractsProgress(
        NcStoreComponent comp,
        NcInventorySnapshot UserSnap,
        NcInventorySnapshot? CrateSnap
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
                    var owned = _logic.GetOwnedFromSnapshot(UserSnap, t.TargetItem, t.MatchMode);

                    if (CrateSnap != null)
                        owned += _logic.GetOwnedFromSnapshot(CrateSnap, t.TargetItem, t.MatchMode);

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
                var owned = _logic.GetOwnedFromSnapshot(UserSnap, contract.TargetItem, contract.MatchMode);

                if (CrateSnap != null)
                    owned += _logic.GetOwnedFromSnapshot(CrateSnap, contract.TargetItem, contract.MatchMode);

                contract.Progress = Math.Min(owned, contract.Required);
            }
        }
    }
}
