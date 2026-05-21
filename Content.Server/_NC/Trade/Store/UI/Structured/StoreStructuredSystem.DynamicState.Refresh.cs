using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;


namespace Content.Server._NC.Trade;


public sealed partial class StoreStructuredSystem : EntitySystem
{
    private void PushDynamicState(
        EntityUid store,
        NcStoreComponent comp,
        DynamicTabState tabs,
        DynamicScratch scratch,
        DynamicStateBuffer buf
    ) =>
        _dynamicStatePublisher.PublishIfChanged(_ui, store, comp, tabs, scratch, buf);

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

    private sealed class StoreDynamicStatePublisher
    {
        public void PublishIfChanged(
            UserInterfaceSystem ui,
            EntityUid store,
            NcStoreComponent comp,
            DynamicTabState tabs,
            DynamicScratch scratch,
            DynamicStateBuffer buf
        )
        {
            if (scratch.EqualsLast(
                buf,
                comp.CatalogRevision,
                tabs.HasBuyTab,
                tabs.HasSellTab,
                tabs.HasBarterTab,
                tabs.HasContractsTab))
                return;

            comp.UiRevision = unchecked(comp.UiRevision + 1);

            ui.SetUiState(
                store,
                StoreUiKey.Key,
                new StoreDynamicState(
                    comp.UiRevision,
                    comp.CatalogRevision,
                    new(buf.BalancesByCurrency),
                    new(buf.RemainingById),
                    new(buf.OwnedById),
                    new(buf.CrateUnitsById),
                    new(buf.CrateTotals),
                    new(buf.Contracts),
                    tabs.HasBuyTab,
                    tabs.HasSellTab,
                    tabs.HasBarterTab,
                    tabs.HasContractsTab,
                    buf.ContractSkipCost,
                    buf.ContractSkipCurrency,
                    scratch.HasVisibleIds,
                    new(buf.ListingScopeIds)
                )
            );

            scratch.Commit(
                comp.CatalogRevision,
                tabs.HasBuyTab,
                tabs.HasSellTab,
                tabs.HasBarterTab,
                tabs.HasContractsTab);
        }
    }
}
