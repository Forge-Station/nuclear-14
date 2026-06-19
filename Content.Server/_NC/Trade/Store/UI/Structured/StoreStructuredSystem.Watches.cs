namespace Content.Server._NC.Trade;


public sealed partial class StoreStructuredSystem
{
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
}
