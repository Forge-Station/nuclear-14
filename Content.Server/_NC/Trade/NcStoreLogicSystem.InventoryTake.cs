using System.Linq;
using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private bool TryTakeProductUnitsInternal(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode)
    {
        if (amount <= 0)
            return true;

        InvalidateInventoryCache(root);

        _scratchItems.Clear();
        foreach (var item in EnumerateDeepItemsUnique(root))
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
            if (IsProtectedFromDirectSale(root, ent))
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
            if (IsProtectedFromDirectSale(root, ent))
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

    private bool TryTakeProductUnitsFromCachedList(
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
            if (IsProtectedFromDirectSale(root, ent))
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
                if (IsProtectedFromDirectSale(root, ent))
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
                if (IsProtectedFromDirectSale(root, ent))
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
                    if (IsProtectedFromDirectSale(root, ent))
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

    private bool TryTakeCurrency(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0)
            return true;

        InvalidateInventoryCache(user);

        var cands = new List<(EntityUid Ent, int Count)>();
        var total = 0;

        foreach (var ent in EnumerateDeepItemsUnique(user))
            if (_ents.TryGetComponent(ent, out StackComponent? st) &&
                st.StackTypeId == stackType)
            {
                var cnt = Math.Max(st.Count, 0);
                if (cnt <= 0)
                    continue;

                cands.Add((ent, cnt));
                total += cnt;
            }

        if (total < amount)
            return false;

        cands.Sort((a, b) => a.Count.CompareTo(b.Count));

        var left = amount;
        foreach (var (ent, have) in cands)
        {
            if (left <= 0)
                break;

            var take = Math.Min(have, left);
            if (_ents.TryGetComponent(ent, out StackComponent? st))
            {
                var newCount = st.Count - take;
                _stacks.SetCount(ent, newCount, st);
                if (newCount <= 0 && _ents.EntityExists(ent))
                    _ents.DeleteEntity(ent);
            }

            left -= take;
        }

        return left <= 0;
    }

    public void GiveCurrency(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0)
            return;

        if (string.IsNullOrWhiteSpace(stackType))
            return;

        InvalidateInventoryCache(user);

        if (!_protos.TryIndex<StackPrototype>(stackType, out var proto))
            return;

        long remaining = amount;

        foreach (var ent in EnumerateDeepItemsUnique(user))
        {
            if (remaining <= 0)
                break;

            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                continue;

            var maxPerStack = proto.MaxCount ?? int.MaxValue;
            if (maxPerStack <= 0)
                maxPerStack = 1;

            var canAdd = (long) maxPerStack - st.Count;
            if (canAdd <= 0)
                continue;

            var add = Math.Min(canAdd, remaining);

            var newCountL = st.Count + add;
            var newCount = (int) Math.Clamp(newCountL, 0L, maxPerStack);

            _stacks.SetCount(ent, newCount, st);
            remaining -= add;
        }

        if (remaining <= 0)
            return;

        var coords = _ents.GetComponent<TransformComponent>(user).Coordinates;

        var perStackLimit = proto.MaxCount ?? int.MaxValue;
        if (perStackLimit <= 0)
            perStackLimit = 1;
        while (remaining > 0)
        {
            var addL = Math.Min(remaining, perStackLimit);
            var add = (int) Math.Clamp(addL, 1L, perStackLimit);

            var spawned = _ents.SpawnEntity(proto.Spawn, coords);

            if (_ents.TryGetComponent(spawned, out StackComponent? newStack))
                _stacks.SetCount(spawned, add, newStack);

            if (_ents.HasComponent<HandsComponent>(user))
                _hands.TryPickupAnyHand(user, spawned, false);

            remaining -= add;
        }

        InvalidateInventoryCache(user);
    }

    private bool TryTakeProductUnitsUnsafe(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode)
    {
        if (!_inventoryCache.TryGetValue(root, out var cachedItems))
        {
            var _ = EnumerateDeepItemsUnique(root).FirstOrDefault();

            if (!_inventoryCache.TryGetValue(root, out cachedItems))
                return false;
        }

        var stackType = GetProductStackType(protoId);
        var effective = ResolveMatchMode(protoId, matchMode);

        bool Matches(EntityPrototype proto)
        {
            if (effective == PrototypeMatchMode.Exact)
                return proto.ID == protoId;
            return proto.ID == protoId || IsProtoOrDescendant(proto, protoId);
        }

        var left = amount;

        for (var i = 0; i < cachedItems.Count && left > 0; i++)
        {
            var ent = cachedItems[i];

            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;

            if (IsProtectedFromDirectSale(root, ent))
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

                if (!Matches(meta.EntityPrototype))
                    continue;

                if (_ents.TryGetComponent(ent, out StackComponent? st) && st.Count > 1)
                    _stacks.SetCount(ent, st.Count - 1, st);
                else
                    _ents.DeleteEntity(ent);

                left -= 1;
            }
        }

        return left <= 0;
    }
}
