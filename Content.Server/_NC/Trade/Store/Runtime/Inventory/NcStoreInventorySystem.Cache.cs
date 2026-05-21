using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Robust.Shared.Containers;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreInventorySystem
{
    private void OnEntityTerminating(ref EntityTerminatingEvent ev)
    {
        // Phase A1 (B21): when the terminating entity was itself a cached root, remove its entry
        // and unlink its items from the reverse-index (those items are still alive, but no longer
        // associated with THIS root).
        if (_inventoryCache.Remove(ev.Entity, out var ownerEntry))
            UnlinkAllReverseEdges(ev.Entity, ownerEntry.Items);

        // Phase A1 (B21): look up which roots had this terminating entity in their cached Items
        // list and bump their revision — O(roots_with_this_item) instead of O(all_roots).
        if (!_rootsByItem.Remove(ev.Entity, out var affectedRoots))
            return;

        foreach (var root in affectedRoots)
            if (_inventoryCache.TryGetValue(root, out var entry))
                entry.Revision = unchecked(entry.Revision + 1);
    }

    public void InvalidateInventoryCache(EntityUid root)
    {
        var entry = GetOrCreateInventoryCacheEntry(root);
        MarkInventoryDirty(entry, false);
    }

    public void InvalidateAllCaches()
    {
        _inventoryCache.Clear();
        _rootsByItem.Clear(); // Phase A1: keep reverse index in sync with main cache.
    }

    public int GetInventoryRevision(EntityUid root) =>
        _inventoryCache.TryGetValue(root, out var entry)
            ? entry.Revision
            : 0;

    private List<EntityUid> GetOrBuildDeepItemsCache(EntityUid owner)
    {
        var entry = GetOrCreateInventoryCacheEntry(owner);
        EnsureItemsCache(owner, entry);
        MarkSnapshotCacheEscaped(entry);
        return entry.Items;
    }

    private List<EntityUid> GetOrBuildDeepItemsCacheCompacted(EntityUid owner)
    {
        var entry = GetOrCreateInventoryCacheEntry(owner);
        EnsureItemsCache(owner, entry);
        CompactCachedItemsIfNeeded(entry.Items);
        MarkSnapshotCacheEscaped(entry);
        return entry.Items;
    }

    private InventoryCacheEntry GetOrCreateInventoryCacheEntry(EntityUid owner)
    {
        if (_inventoryCache.TryGetValue(owner, out var entry))
            return entry;

        entry = new();
        _inventoryCache[owner] = entry;
        return entry;
    }

    private void EnsureItemsCache(EntityUid owner, InventoryCacheEntry entry)
    {
        if (entry.ItemsRevision == entry.Revision)
            return;

        BuildDeepItemsCache(owner, entry.Items);
        entry.ItemsRevision = entry.Revision;
    }

    private void EnsureSnapshotCache(EntityUid owner, InventoryCacheEntry entry)
    {
        EnsureItemsCache(owner, entry);
        if (entry.SnapshotRevision == entry.Revision)
            return;

        FillInventorySnapshotFromItems(owner, entry.Items, entry.Snapshot);
        entry.SnapshotRevision = entry.Revision;
    }

    private static void MarkSnapshotCacheEscaped(InventoryCacheEntry entry) =>
        // Callers receive the live internal items list and may mutate it in-place.
        entry.SnapshotRevision = UncachedRevision;

    private static void MarkInventoryDirty(InventoryCacheEntry entry, bool itemsStillCurrent)
    {
        entry.Revision = unchecked(entry.Revision + 1);
        entry.ItemsRevision = itemsStillCurrent ? entry.Revision : UncachedRevision;
        entry.SnapshotRevision = UncachedRevision;
    }

    private void BuildDeepItemsCache(EntityUid owner, List<EntityUid> cached)
    {
        _scratchVisited.Clear();
        _scratchQueue.Clear();
        _scratchResult.Clear();

        void Enqueue(EntityUid uid)
        {
            if (uid == EntityUid.Invalid)
                return;
            if (!_scratchVisited.Add(uid))
                return;
            _scratchQueue.Enqueue(uid);
            _scratchResult.Add(uid);
        }

        if (_ents.TryGetComponent(owner, out InventoryComponent? inventory))
        {
            var slotEnum = new InventorySystem.InventorySlotEnumerator(inventory);
            while (slotEnum.NextItem(out var item))
                Enqueue(item);
        }

        if (_ents.TryGetComponent(owner, out ItemSlotsComponent? itemSlots))
        {
            foreach (var slot in itemSlots.Slots.Values)
                if (slot is { HasItem: true, Item: not null, })
                    Enqueue(slot.Item.Value);
        }

        if (_ents.TryGetComponent(owner, out HandsComponent? hands))
        {
            foreach (var hand in hands.Hands.Values)
                if (hand.HeldEntity.HasValue)
                    Enqueue(hand.HeldEntity.Value);
        }

        if (_ents.TryGetComponent(owner, out ContainerManagerComponent? cmcRoot))
        {
            foreach (var container in cmcRoot.Containers.Values)
                foreach (var entity in container.ContainedEntities)
                    Enqueue(entity);
        }

        while (_scratchQueue.Count > 0)
        {
            var current = _scratchQueue.Dequeue();
            if (!_ents.TryGetComponent(current, out ContainerManagerComponent? cmc))
                continue;

            foreach (var container in cmc.Containers.Values)
                foreach (var child in container.ContainedEntities)
                    Enqueue(child);
        }

        RefreshReverseIndexForRebuild(owner, cached, _scratchResult);

        cached.Clear();
        if (cached.Capacity < _scratchResult.Count)
            cached.Capacity = _scratchResult.Count;
        cached.AddRange(_scratchResult);
    }

    private void RefreshReverseIndexForRebuild(
        EntityUid owner,
        List<EntityUid> oldItems,
        List<EntityUid> newItems
    )
    {
        _rebuildOldItemsScratch.Clear();
        for (var i = 0; i < oldItems.Count; i++)
        {
            var ent = oldItems[i];
            if (ent != EntityUid.Invalid)
                _rebuildOldItemsScratch.Add(ent);
        }

        for (var i = 0; i < newItems.Count; i++)
        {
            var ent = newItems[i];
            if (ent == EntityUid.Invalid)
                continue;

            if (_rebuildOldItemsScratch.Remove(ent))
                continue;

            if (!_rootsByItem.TryGetValue(ent, out var rootsSet))
            {
                rootsSet = new();
                _rootsByItem[ent] = rootsSet;
            }

            rootsSet.Add(owner);
        }

        foreach (var droppedItem in _rebuildOldItemsScratch)
        {
            if (!_rootsByItem.TryGetValue(droppedItem, out var rootsSet))
                continue;

            rootsSet.Remove(owner);
            if (rootsSet.Count == 0)
                _rootsByItem.Remove(droppedItem);
        }

        _rebuildOldItemsScratch.Clear();
    }

    private void UnlinkAllReverseEdges(EntityUid owner, List<EntityUid> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == EntityUid.Invalid)
                continue;

            if (!_rootsByItem.TryGetValue(item, out var rootsSet))
                continue;

            rootsSet.Remove(owner);
            if (rootsSet.Count == 0)
                _rootsByItem.Remove(item);
        }
    }

    private void CompactCachedItems(List<EntityUid> cached)
    {
        var w = 0;
        for (var r = 0; r < cached.Count; r++)
        {
            var ent = cached[r];
            if (ent != EntityUid.Invalid && _ents.EntityExists(ent))
                cached[w++] = ent;
        }

        if (w < cached.Count)
            cached.RemoveRange(w, cached.Count - w);
    }

    private void CompactCachedItemsIfNeeded(List<EntityUid> cached)
    {
        if (cached.Count < 256)
            return;

        var invalid = 0;
        var threshold = Math.Max(64, cached.Count / 4);

        for (var i = 0; i < cached.Count; i++)
        {
            var ent = cached[i];
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
            {
                invalid++;
                if (invalid >= threshold)
                    break;
            }
        }

        if (invalid < threshold)
            return;

        CompactCachedItems(cached);
    }

    public NcInventorySnapshot BuildInventorySnapshot(EntityUid root)
    {
        var snap = new NcInventorySnapshot();
        FillInventorySnapshot(root, snap);
        return snap;
    }

    public void FillInventorySnapshot(EntityUid root, NcInventorySnapshot buffer)
    {
        var entry = GetOrCreateInventoryCacheEntry(root);
        EnsureSnapshotCache(root, entry);
        buffer.CopyFrom(entry.Snapshot);
    }

    public void ScanInventory(EntityUid root, List<EntityUid> itemsBuffer, NcInventorySnapshot snapshotBuffer)
    {
        var entry = GetOrCreateInventoryCacheEntry(root);
        EnsureItemsCache(root, entry);
        CompactCachedItemsIfNeeded(entry.Items);

        itemsBuffer.Clear();
        itemsBuffer.AddRange(entry.Items);

        EnsureSnapshotCache(root, entry);
        snapshotBuffer.CopyFrom(entry.Snapshot);
    }

    public void ScanInventoryItems(EntityUid root, List<EntityUid> itemsBuffer)
    {
        var entry = GetOrCreateInventoryCacheEntry(root);
        EnsureItemsCache(root, entry);
        CompactCachedItemsIfNeeded(entry.Items);

        itemsBuffer.Clear();
        itemsBuffer.AddRange(entry.Items);
    }

    private void FillInventorySnapshotFromItems(
        EntityUid root,
        IReadOnlyList<EntityUid> items,
        NcInventorySnapshot buffer
    )
    {
        buffer.Clear();
        foreach (var ent in items)
        {
            if (!_ents.EntityExists(ent))
                continue;
            if (IsProtectedFromDirectSale(root, ent))
                continue;

            _ents.TryGetComponent(ent, out MetaDataComponent? meta);
            var proto = meta?.EntityPrototype;

            if (_ents.TryGetComponent(ent, out StackComponent? stack))
            {
                var cnt = Math.Max(stack.Count, 0);
                if (cnt > 0 && !string.IsNullOrWhiteSpace(stack.StackTypeId))
                {
                    buffer.StackTypeCounts.TryGetValue(stack.StackTypeId, out var prev);
                    buffer.StackTypeCounts[stack.StackTypeId] = prev + cnt;
                }

                if (cnt > 0 && proto != null)
                {
                    if (!buffer.ProtoCounts.TryAdd(proto.ID, cnt))
                        buffer.ProtoCounts[proto.ID] += cnt;
                }

                continue;
            }

            if (proto == null)
                continue;

            if (!buffer.ProtoCounts.TryAdd(proto.ID, 1))
                buffer.ProtoCounts[proto.ID] += 1;
        }
    }
}
