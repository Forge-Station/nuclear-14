using Content.Shared._NC.Trade;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreInventorySystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-inventory");
    private const int UncachedRevision = int.MinValue;

    private sealed class InventoryCacheEntry
    {
        public readonly List<EntityUid> Items = new();
        public readonly NcInventorySnapshot Snapshot = new();
        public int Revision;
        public int ItemsRevision = UncachedRevision;
        public int SnapshotRevision = UncachedRevision;
    }

    private readonly record struct ProductTakeRequest(
        string ProtoId,
        string? StackType,
        PrototypeMatchMode MatchMode,
        CompiledMatcher? Matcher,
        bool IsValid);

    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    private readonly Dictionary<EntityUid, InventoryCacheEntry> _inventoryCache = new();
    [Dependency] private readonly TagSystem _tags = default!;

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _rootsByItem = new();
    private readonly HashSet<EntityUid> _rebuildOldItemsScratch = new();

    private readonly Dictionary<string, string?> _productStackTypeCache = new(StringComparer.Ordinal);
    [Dependency] private readonly IPrototypeManager _protos = default!;
    private readonly Queue<EntityUid> _scratchQueue = new();
    private readonly List<EntityUid> _scratchResult = new();
    private readonly HashSet<EntityUid> _scratchVisited = new();
    [Dependency] private readonly SharedStackSystem _stacks = default!;

    public override void Initialize()
    {
        base.Initialize();
        _protos.PrototypesReloaded += OnPrototypesReloaded;
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
        SubscribeLocalEvent<EntParentChangedMessage>(OnEntityParentChanged);
    }

    private void OnEntityParentChanged(ref EntParentChangedMessage ev)
    {
        if (!_rootsByItem.TryGetValue(ev.Entity, out var affectedRoots) || affectedRoots.Count == 0)
            return;

        foreach (var root in affectedRoots)
        {
            if (_inventoryCache.TryGetValue(root, out var entry))
            {
                MarkInventoryDirty(entry, itemsStillCurrent: false);
            }
        }
    }

    public override void Shutdown()
    {
        _protos.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        _productStackTypeCache.Clear();
        _compiledMatcherCache.Clear();
        InvalidateAllCaches();
    }

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
        {
            if (_inventoryCache.TryGetValue(root, out var entry))
                entry.Revision = unchecked(entry.Revision + 1);
        }
    }


    public void InvalidateInventoryCache(EntityUid root)
    {
        var entry = GetOrCreateInventoryCacheEntry(root);
        MarkInventoryDirty(entry, itemsStillCurrent: false);
    }

    public void InvalidateAllCaches()
    {
        _inventoryCache.Clear();
        _rootsByItem.Clear(); // Phase A1: keep reverse index in sync with main cache.
    }

    public int GetInventoryRevision(EntityUid root)
    {
        return _inventoryCache.TryGetValue(root, out var entry)
            ? entry.Revision
            : 0;
    }

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

    private static void MarkSnapshotCacheEscaped(InventoryCacheEntry entry)
    {
        // Callers receive the live internal items list and may mutate it in-place.
        entry.SnapshotRevision = UncachedRevision;
    }

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
                if (slot is { HasItem: true, Item: not null })
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
        List<EntityUid> newItems)
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
                rootsSet = new HashSet<EntityUid>();
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

    public int GetOwnedFromSnapshot(
        in NcInventorySnapshot snapshot,
        string productProtoId,
        PrototypeMatchMode matchMode
    )
    {
        if (matchMode == PrototypeMatchMode.Matcher)
        {
            var matcher = GetCompiledMatcher(productProtoId, warnIfInvalid: false);
            return matcher == null ? 0 : GetOwnedFromSnapshotForCompiledMatcher(snapshot, matcher);
        }

        var stackType = GetProductStackType(productProtoId);
        if (stackType != null)
            return snapshot.StackTypeCounts.TryGetValue(stackType, out var cnt) ? cnt : 0;

        return snapshot.ProtoCounts.TryGetValue(productProtoId, out var exact) ? exact : 0;
    }

    public int GetOwnedFromRootCached(
        EntityUid root,
        string protoId,
        PrototypeMatchMode matchMode)
    {
        var request = CreateProductTakeRequest(protoId, matchMode);
        if (!request.IsValid)
            return 0;

        var cachedItems = GetOrBuildDeepItemsCache(root);
        return CalculateAvailableTakeUnits(root, cachedItems, request, int.MaxValue);
    }


    public bool TryTakeProductUnitsFromRootCached(
        EntityUid root,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        if (amount <= 0)
            return true;
        var cachedItems = GetOrBuildDeepItemsCache(root);
        return TryTakeProductUnitsFromCachedList(root, cachedItems, protoId, amount, matchMode);
    }

    public bool TryTakeProductUnitsFromCachedList(
        EntityUid root,
        List<EntityUid> cachedItems,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        if (amount <= 0)
            return true;

        var request = CreateProductTakeRequest(protoId, matchMode);
        if (!request.IsValid)
            return false;

        if (CalculateAvailableTakeUnits(root, cachedItems, request, amount) < amount)
            return false;

        var success = ExecuteTakeUnitsFromCachedItems(root, cachedItems, request, amount);
        if (success && _inventoryCache.TryGetValue(root, out var entry))
            MarkInventoryDirty(entry, ReferenceEquals(entry.Items, cachedItems));

        return success;
    }

    private ProductTakeRequest CreateProductTakeRequest(string protoId, PrototypeMatchMode matchMode)
    {
        if (matchMode == PrototypeMatchMode.Matcher)
        {
            var matcher = GetCompiledMatcher(protoId, warnIfInvalid: true);
            if (matcher == null)
            {
                return new(
                    protoId,
                    null,
                    matchMode,
                    null,
                    false);
            }

            return new(
                protoId,
                null,
                matchMode,
                matcher,
                true);
        }

        return new(
            protoId,
            GetProductStackType(protoId),
            matchMode,
            null,
            true);
    }

    private int CalculateAvailableTakeUnits(
        EntityUid root,
        IReadOnlyList<EntityUid> cachedItems,
        ProductTakeRequest request,
        int maxNeeded)
    {
        var availableTotal = 0;

        foreach (var ent in cachedItems)
        {
            if (ShouldSkipTakeEntity(root, ent))
                continue;

            availableTotal += CountTakeableUnits(ent, request);
            if (availableTotal >= maxNeeded)
                break;
        }

        return availableTotal;
    }

    private bool ExecuteTakeUnitsFromCachedItems(
        EntityUid root,
        List<EntityUid> cachedItems,
        ProductTakeRequest request,
        int amount)
    {
        var left = amount;
        var compactNeeded = false;

        for (var i = 0; i < cachedItems.Count && left > 0; i++)
        {
            if (!TryConsumeTakeUnitsFromEntity(root, cachedItems, i, request, ref left, ref compactNeeded))
                continue;
        }

        if (compactNeeded)
            CompactCachedItemsIfNeeded(cachedItems);

        return left <= 0;
    }

    private bool TryConsumeTakeUnitsFromEntity(
        EntityUid root,
        List<EntityUid> cachedItems,
        int index,
        ProductTakeRequest request,
        ref int left,
        ref bool compactNeeded)
    {
        var ent = cachedItems[index];
        if (ShouldSkipTakeEntity(root, ent))
            return false;

        if (request.StackType != null)
            return TryConsumeStackTypeTake(cachedItems, index, ent, request.StackType, ref left, ref compactNeeded);

        return TryConsumePrototypeTake(cachedItems, index, ent, request, ref left, ref compactNeeded);
    }

    private bool ShouldSkipTakeEntity(EntityUid root, EntityUid ent)
    {
        return ent == EntityUid.Invalid || !_ents.EntityExists(ent) || IsProtectedFromDirectSale(root, ent);
    }

    private int CountTakeableUnits(EntityUid ent, ProductTakeRequest request)
    {
        if (request.StackType != null)
            return CountTakeableStackUnits(ent, request.StackType);

        return CountTakeablePrototypeUnits(ent, request);
    }

    private int CountTakeableStackUnits(EntityUid ent, string stackType)
    {
        if (_ents.TryGetComponent(ent, out StackComponent? stack) && stack.StackTypeId == stackType)
            return Math.Max(stack.Count, 0);

        return 0;
    }

    private int CountTakeablePrototypeUnits(EntityUid ent, ProductTakeRequest request)
    {
        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return 0;

        if (!MatchesTakeRequest(ent, meta.EntityPrototype, request))
            return 0;

        if (_ents.TryGetComponent(ent, out StackComponent? stack) && stack.Count > 0)
            return stack.Count;

        return 1;
    }

    private bool TryConsumeStackTypeTake(
        List<EntityUid> cachedItems,
        int index,
        EntityUid ent,
        string stackType,
        ref int left,
        ref bool compactNeeded)
    {
        if (!_ents.TryGetComponent(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
            return false;

        var have = Math.Max(stack.Count, 0);
        if (have <= 0)
            return false;

        ConsumeStackUnits(cachedItems, index, ent, stack, ref left, ref compactNeeded);
        return true;
    }

    private bool TryConsumePrototypeTake(
        List<EntityUid> cachedItems,
        int index,
        EntityUid ent,
        ProductTakeRequest request,
        ref int left,
        ref bool compactNeeded)
    {
        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return false;

        if (!MatchesTakeRequest(ent, meta.EntityPrototype, request))
            return false;

        if (_ents.TryGetComponent(ent, out StackComponent? stack))
        {
            ConsumeStackUnits(cachedItems, index, ent, stack, ref left, ref compactNeeded);
            return true;
        }

        DeleteConsumedEntity(cachedItems, index, ent, ref left, ref compactNeeded);
        return true;
    }

    private bool MatchesTakeRequest(EntityUid ent, EntityPrototype proto, ProductTakeRequest request)
    {
        if (request.MatchMode == PrototypeMatchMode.Matcher)
        {
            if (request.Matcher == null)
                return false;

            if (request.Matcher.Items.Contains(proto.ID))
                return true;

            if (_ents.TryGetComponent(ent, out StackComponent? stack) &&
                MatcherMatchesStackType(request.Matcher, stack.StackTypeId))
            {
                return true;
            }

            if (request.Matcher.Tags.Count == 0)
                return false;

            if (!TryComp<TagComponent>(ent, out var tagComponent))
                return false;

            for (var i = 0; i < request.Matcher.Tags.Count; i++)
            {
                if (_tags.HasTag(tagComponent, request.Matcher.Tags[i]))
                    return true;
            }

            return false;
        }

        return proto.ID == request.ProtoId;
    }

    private void ConsumeStackUnits(
        List<EntityUid> cachedItems,
        int index,
        EntityUid ent,
        StackComponent stack,
        ref int left,
        ref bool compactNeeded)
    {
        var have = Math.Max(stack.Count, 0);
        var take = Math.Min(have, left);
        _stacks.SetCount(ent, have - take, stack);

        if (stack.Count <= 0)
            DeleteConsumedEntity(cachedItems, index, ent, ref compactNeeded);

        left -= take;
    }

    private void DeleteConsumedEntity(
        List<EntityUid> cachedItems,
        int index,
        EntityUid ent,
        ref int left,
        ref bool compactNeeded)
    {
        DeleteConsumedEntity(cachedItems, index, ent, ref compactNeeded);
        left -= 1;
    }

    private void DeleteConsumedEntity(
        List<EntityUid> cachedItems,
        int index,
        EntityUid ent,
        ref bool compactNeeded)
    {
        _ents.DeleteEntity(ent);
        cachedItems[index] = EntityUid.Invalid;
        compactNeeded = true;
    }


    public bool IsProtectedFromDirectSale(EntityUid root, EntityUid item)
    {
        if (!_ents.HasComponent<InventoryComponent>(root))
            return false;

        if (!IsDirectChildOf(root, item))
            return false;
        if (IsHeldInHands(root, item))
            return false;

        return _ents.HasComponent<ClothingComponent>(item);
    }

    private bool IsDirectChildOf(EntityUid root, EntityUid item) =>
        _ents.TryGetComponent(item, out TransformComponent? xform) && xform.ParentUid == root;

    private bool IsHeldInHands(EntityUid user, EntityUid item)
    {
        if (!_ents.TryGetComponent(user, out HandsComponent? hands))
            return false;
        foreach (var hand in hands.Hands.Values)
            if (hand.HeldEntity == item)
                return true;
        return false;
    }

    public bool EntityMatchesItemGroup(EntityUid entity, NcItemGroupPrototype group)
    {
        if (!_ents.TryGetComponent(entity, out MetaDataComponent? meta) ||
            meta.EntityPrototype == null)
        {
            return false;
        }

        var protoId = meta.EntityPrototype.ID;
        var matcher = new CompiledMatcher(group.Prototypes, group.Tags);

        if (matcher.Items.Contains(protoId))
            return true;

        if (_ents.TryGetComponent(entity, out StackComponent? stack) &&
            MatcherMatchesStackType(matcher, stack.StackTypeId))
        {
            return true;
        }

        if (matcher.Tags.Count == 0)
            return false;

        return MatcherPrototypeHasAnyTag(matcher, protoId);
    }

    public string? GetProductStackType(string productProtoId)
    {
        if (_productStackTypeCache.TryGetValue(productProtoId, out var cached))
            return cached;

        string? stackType = null;
        if (_protos.TryIndex<EntityPrototype>(productProtoId, out var proto))
        {
            var stackName = _compFactory.GetComponentName(typeof(StackComponent));
            if (proto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                stackType = prodStackDef.StackTypeId;
        }

        _productStackTypeCache[productProtoId] = stackType;
        return stackType;
    }

    private bool ProtoHasAnyMatcherTag(string protoId, IReadOnlyList<string> matcherTags)
    {
        if (matcherTags.Count == 0)
            return false;

        if (!_protos.TryIndex<EntityPrototype>(protoId, out var proto))
            return false;

        if (!proto.TryGetComponent(out TagComponent? tagComponent, _compFactory) || tagComponent == null)
            return false;

        for (var i = 0; i < matcherTags.Count; i++)
        {
            if (_tags.HasTag(tagComponent, matcherTags[i]))
                return true;
        }

        return false;
    }

}
