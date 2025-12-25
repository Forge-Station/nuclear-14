using Content.Shared._NC.Trade;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed class NcInventoryHelper
{
    private readonly IComponentFactory _compFactory;
    private readonly IEntityManager _ents;
    private readonly Dictionary<EntityUid, List<EntityUid>> _inventoryCache = new();
    private readonly Dictionary<string, string?> _productStackTypeCache = new();
    private readonly Dictionary<string, string[]> _protoAndAncestorsCache = new();
    private readonly IPrototypeManager _protos;
    private readonly List<EntityUid> _scratchItems = new();
    private readonly Queue<EntityUid> _scratchQueue = new();
    private readonly List<EntityUid> _scratchResult = new();
    private readonly HashSet<EntityUid> _scratchVisited = new();
    private readonly SharedStackSystem _stacks;

    public NcInventoryHelper(
        IEntityManager ents,
        IPrototypeManager protos,
        IComponentFactory compFactory,
        SharedStackSystem stacks
    )
    {
        _ents = ents;
        _protos = protos;
        _compFactory = compFactory;
        _stacks = stacks;
    }

    public void OnPrototypesReloaded()
    {
        _productStackTypeCache.Clear();
        _protoAndAncestorsCache.Clear();
        _inventoryCache.Clear();
    }

    public void ResetFrameCache() => _inventoryCache.Clear();

    public void OnEntityTerminating(EntityUid entity) => _inventoryCache.Remove(entity);

    public void InvalidateInventoryCache(EntityUid root) => _inventoryCache.Remove(root);

    public IEnumerable<EntityUid> EnumerateDeepItems(EntityUid owner)
    {
        if (_inventoryCache.TryGetValue(owner, out var cached))
        {
            foreach (var ent in cached)
                if (_ents.EntityExists(ent))
                    yield return ent;

            yield break;
        }

        _scratchVisited.Clear();
        _scratchQueue.Clear();
        _scratchResult.Clear();

        void Enqueue(EntityUid uid)
        {
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
            {
                foreach (var entity in container.ContainedEntities)
                    Enqueue(entity);
            }
        }

        while (_scratchQueue.Count > 0)
        {
            var current = _scratchQueue.Dequeue();

            if (_ents.TryGetComponent(current, out ContainerManagerComponent? cmc))
            {
                foreach (var container in cmc.Containers.Values)
                {
                    foreach (var child in container.ContainedEntities)
                        Enqueue(child);
                }
            }
        }

        var cachedList = new List<EntityUid>(_scratchResult.Count);
        cachedList.AddRange(_scratchResult);
        _inventoryCache[owner] = cachedList;

        foreach (var ent in cachedList)
            if (_ents.EntityExists(ent))
                yield return ent;
    }

    public bool TryGetCachedDeepItems(EntityUid root, out List<EntityUid> cachedItems)
    {
        if (_inventoryCache.TryGetValue(root, out cachedItems!))
            return true;

        foreach (var _ in EnumerateDeepItems(root))
            break;

        return _inventoryCache.TryGetValue(root, out cachedItems!);
    }


    public bool IsTradable(EntityUid root, EntityUid item)
    {
        if (!_ents.HasComponent<InventoryComponent>(root))
            return true;

        if (!IsDirectChildOf(root, item))
            return true;

        if (IsHeldInHands(root, item))
            return true;

        return !_ents.HasComponent<ClothingComponent>(item);
    }

    private bool IsHeldInHands(EntityUid user, EntityUid item)
    {
        if (!_ents.TryGetComponent(user, out HandsComponent? hands))
            return false;

        foreach (var hand in hands.Hands.Values)
            if (hand.HeldEntity == item)
                return true;

        return false;
    }

    private bool IsDirectChildOf(EntityUid root, EntityUid item) =>
        _ents.TryGetComponent(item, out TransformComponent? xform) && xform.ParentUid == root;

    public PrototypeMatchMode ResolveMatchMode(string expectedProtoId, PrototypeMatchMode configured)
    {
        if (configured == PrototypeMatchMode.Descendants)
            return PrototypeMatchMode.Descendants;

        if (_protos.TryIndex<EntityPrototype>(expectedProtoId, out var expectedProto) && expectedProto.Abstract)
            return PrototypeMatchMode.Descendants;

        return PrototypeMatchMode.Exact;
    }

    public int GetOwnedFromSnapshot(
        in NcStoreLogicSystem.InventorySnapshot snapshot,
        string productProtoId,
        PrototypeMatchMode matchMode
    )
    {
        var stackType = GetProductStackType(productProtoId);
        if (stackType != null)
            return snapshot.StackTypeCounts.TryGetValue(stackType, out var cnt) ? cnt : 0;

        var effective = ResolveMatchMode(productProtoId, matchMode);

        if (effective == PrototypeMatchMode.Descendants)
            return snapshot.AncestorCounts.TryGetValue(productProtoId, out var units) ? units : 0;

        return snapshot.ProtoCounts.TryGetValue(productProtoId, out var exact) ? exact : 0;
    }

    public string? GetProductStackType(string productProtoId)
    {
        if (_productStackTypeCache.TryGetValue(productProtoId, out var cached))
            return cached;

        string? stackType = null;

        if (_protos.TryIndex<EntityPrototype>(productProtoId, out var expectedProto))
        {
            var stackName = _compFactory.GetComponentName(typeof(StackComponent));
            if (expectedProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                stackType = prodStackDef.StackTypeId;
        }

        _productStackTypeCache[productProtoId] = stackType;
        return stackType;
    }

    public string[] GetProtoAndAncestors(EntityPrototype proto)
    {
        var id = proto.ID;
        if (_protoAndAncestorsCache.TryGetValue(id, out var cached))
            return cached;

        var visited = new HashSet<string>();
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(id);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!visited.Add(cur))
                continue;
            result.Add(cur);

            if (!_protos.TryIndex<EntityPrototype>(cur, out var curProto) || curProto.Parents == null)
                continue;

            foreach (var p in curProto.Parents)
                if (!string.IsNullOrWhiteSpace(p))
                    stack.Push(p);
        }

        var arr = result.ToArray();
        _protoAndAncestorsCache[id] = arr;
        return arr;
    }

    public bool IsProtoOrDescendant(EntityPrototype candidate, string expectedId)
    {
        if (candidate.ID == expectedId)
            return true;

        var ancestors = GetProtoAndAncestors(candidate);
        foreach (var t in ancestors)
            if (t == expectedId)
                return true;

        return false;
    }

    public bool TryTakeProductUnitsInternal(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode)
    {
        if (amount <= 0)
            return true;

        InvalidateInventoryCache(root);

        _scratchItems.Clear();
        foreach (var item in EnumerateDeepItems(root))
            _scratchItems.Add(item);

        var allItems = _scratchItems;
        var stackType = GetProductStackType(protoId);
        var availableTotal = 0;
        var effective = ResolveMatchMode(protoId, matchMode);

        bool Matches(EntityPrototype proto)
        {
            if (effective == PrototypeMatchMode.Exact)
                return proto.ID == protoId;
            return proto.ID == protoId || IsProtoOrDescendant(proto, protoId);
        }

        foreach (var ent in allItems)
        {
            if (!_ents.EntityExists(ent))
                continue;
            if (!IsTradable(root, ent))
                continue;

            if (stackType != null)
            {
                if (_ents.TryGetComponent(ent, out StackComponent? stack) && stack.StackTypeId == stackType)
                    availableTotal += Math.Max(stack.Count, 0);
            }
            else
            {
                if (_ents.TryGetComponent(ent, out StackComponent? stack))
                {
                    if (_ents.TryGetComponent(ent, out MetaDataComponent? meta) && meta.EntityPrototype != null &&
                        Matches(meta.EntityPrototype))
                        availableTotal += stack.Count;
                }
                else if (_ents.TryGetComponent(ent, out MetaDataComponent? meta) && meta.EntityPrototype != null)
                {
                    if (Matches(meta.EntityPrototype))
                        availableTotal += 1;
                }
            }

            if (availableTotal >= amount)
                break;
        }

        if (availableTotal < amount)
            return false;

        var left = amount;

        foreach (var ent in allItems)
        {
            if (left <= 0)
                break;
            if (!_ents.EntityExists(ent))
                continue;
            if (!IsTradable(root, ent))
                continue;

            if (stackType != null)
            {
                if (!_ents.TryGetComponent(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
                    continue;

                var have = Math.Max(stack.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, left);
                var newCount = have - take;
                _stacks.SetCount(ent, newCount, stack);

                if (newCount <= 0 && _ents.EntityExists(ent))
                    _ents.DeleteEntity(ent);

                left -= take;
            }
            else
            {
                if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                    continue;

                var matches = effective == PrototypeMatchMode.Exact
                    ? meta.EntityPrototype.ID == protoId
                    : Matches(meta.EntityPrototype);

                if (!matches)
                    continue;

                if (_ents.TryGetComponent(ent, out StackComponent? st))
                {
                    var have = st.Count;
                    var take = Math.Min(have, left);

                    if (take >= have)
                        _ents.DeleteEntity(ent);
                    else
                        _stacks.SetCount(ent, have - take, st);

                    left -= take;
                }
                else
                {
                    _ents.DeleteEntity(ent);
                    left -= 1;
                }
            }
        }

        InvalidateInventoryCache(root);
        return left <= 0;
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

        var stackType = GetProductStackType(protoId);
        var availableTotal = 0;

        var effective = ResolveMatchMode(protoId, matchMode);

        bool Matches(EntityPrototype proto)
        {
            if (effective == PrototypeMatchMode.Exact)
                return proto.ID == protoId;
            return proto.ID == protoId || IsProtoOrDescendant(proto, protoId);
        }

        foreach (var ent in cachedItems)
        {
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;
            if (!IsTradable(root, ent))
                continue;

            if (stackType != null)
            {
                if (_ents.TryGetComponent(ent, out StackComponent? stack) && stack.StackTypeId == stackType)
                    availableTotal += Math.Max(stack.Count, 0);
            }
            else
            {
                if (_ents.TryGetComponent(ent, out MetaDataComponent? meta) && meta.EntityPrototype != null)
                {
                    if (Matches(meta.EntityPrototype))
                    {
                        if (_ents.TryGetComponent(ent, out StackComponent? st) && st.Count > 0)
                            availableTotal += st.Count;
                        else
                            availableTotal += 1;
                    }
                }
            }

            if (availableTotal >= amount)
                break;
        }

        if (availableTotal < amount)
            return false;

        var left = amount;

        if (stackType != null)
        {
            for (var i = 0; i < cachedItems.Count && left > 0; i++)
            {
                var ent = cachedItems[i];
                if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                    continue;
                if (!IsTradable(root, ent))
                    continue;

                if (!_ents.TryGetComponent(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
                    continue;

                var have = Math.Max(stack.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, left);
                var newCount = have - take;
                _stacks.SetCount(ent, newCount, stack);

                if (newCount <= 0 && _ents.EntityExists(ent))
                {
                    _ents.DeleteEntity(ent);
                    cachedItems[i] = EntityUid.Invalid;
                }

                left -= take;
            }
        }
        else
        {
            for (var i = 0; i < cachedItems.Count && left > 0; i++)
            {
                var ent = cachedItems[i];
                if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                    continue;
                if (!IsTradable(root, ent))
                    continue;
                if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                    continue;

                if (meta.EntityPrototype.ID == protoId)
                    DeleteOrDecrement(i, ent);
            }

            if (left > 0 && effective != PrototypeMatchMode.Exact)
            {
                for (var i = 0; i < cachedItems.Count && left > 0; i++)
                {
                    var ent = cachedItems[i];
                    if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                        continue;
                    if (!IsTradable(root, ent))
                        continue;
                    if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                        continue;

                    if (meta.EntityPrototype.ID == protoId)
                        continue;

                    if (Matches(meta.EntityPrototype))
                        DeleteOrDecrement(i, ent);
                }
            }

            void DeleteOrDecrement(int index, EntityUid item)
            {
                if (_ents.TryGetComponent(item, out StackComponent? st))
                {
                    var have = Math.Max(st.Count, 0);
                    if (have <= 1)
                    {
                        if (have > 0)
                            left -= 1;
                        if (_ents.EntityExists(item))
                            _ents.DeleteEntity(item);
                        cachedItems[index] = EntityUid.Invalid;
                        return;
                    }

                    var take = Math.Min(have, left);
                    var newCount = have - take;
                    _stacks.SetCount(item, newCount, st);

                    if (newCount <= 0 && _ents.EntityExists(item))
                    {
                        _ents.DeleteEntity(item);
                        cachedItems[index] = EntityUid.Invalid;
                    }

                    left -= take;
                    return;
                }

                if (_ents.EntityExists(item))
                    _ents.DeleteEntity(item);
                cachedItems[index] = EntityUid.Invalid;
                left -= 1;
            }
        }

        return left <= 0;
    }
}
