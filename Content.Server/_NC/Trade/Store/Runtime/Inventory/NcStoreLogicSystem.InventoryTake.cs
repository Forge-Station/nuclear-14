using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    /// <summary>
    ///     Atomic consume: first validates availability, then performs entity mutations.
    ///     Uses the per-root deep-items cache to avoid repeated traversals.
    /// </summary>
    private bool TryTakeProductUnitsFromRootCached(
        EntityUid root,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        if (amount <= 0)
            return true;

        var cachedItems = GetOrBuildDeepItemsCache(root);
        var ok = TryTakeProductUnitsFromCachedList(root, cachedItems, protoId, amount, matchMode);
        return ok;
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

    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount) =>
        TryTakeProductUnitsFromRootCached(user, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsFromRootCached(user, protoId, amount, matchMode);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount) =>
        TryTakeProductUnitsFromRootCached(root, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsFromRootCached(root, protoId, amount, matchMode);

}
