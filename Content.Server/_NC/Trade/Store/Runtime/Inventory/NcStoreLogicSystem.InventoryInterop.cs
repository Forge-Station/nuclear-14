using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{

    public void FillDeepItemsList(EntityUid root, List<EntityUid> buffer)
    {
        buffer.Clear();
        foreach (var ent in EnumerateDeepItemsUnique(root))
            buffer.Add(ent);
    }

    public void FillInventorySnapshotFromItems(EntityUid root, IReadOnlyList<EntityUid> items, InventorySnapshot buffer)
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

                    foreach (var id in GetProtoAndAncestors(proto))
                    {
                        buffer.AncestorCounts.TryGetValue(id, out var prev);
                        buffer.AncestorCounts[id] = prev + cnt;
                    }
                }

                continue;
            }

            if (proto is null)
                continue;

            if (!buffer.ProtoCounts.TryAdd(proto.ID, 1))
                buffer.ProtoCounts[proto.ID] += 1;

            foreach (var id in GetProtoAndAncestors(proto))
            {
                buffer.AncestorCounts.TryGetValue(id, out var prev);
                buffer.AncestorCounts[id] = prev + 1;
            }
        }
    }

    public bool TryTakeProductUnitsFromCachedItems(
        EntityUid root,
        List<EntityUid> cachedItems,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    ) =>
        TryTakeProductUnitsFromCachedList(root, cachedItems, protoId, amount, matchMode);

}
