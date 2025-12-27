using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public void FillDeepItemsList(EntityUid root, List<EntityUid> buffer)
    {
        buffer.Clear();
        var cached = GetOrBuildDeepItemsCache(root);
        CompactCachedItems(cached);
        for (var i = 0; i < cached.Count; i++)
        {
            var ent = cached[i];
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;
            buffer.Add(ent);
        }
    }

    public void ScanInventory(
        EntityUid root,
        List<EntityUid> itemsBuffer,
        InventorySnapshot snapshotBuffer,
        bool compactCache = true)
    {
        itemsBuffer.Clear();

        var cached = GetOrBuildDeepItemsCache(root);
        if (compactCache)
            CompactCachedItems(cached);

        for (var i = 0; i < cached.Count; i++)
        {
            var ent = cached[i];
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;

            itemsBuffer.Add(ent);
        }

        FillInventorySnapshotFromItems(root, itemsBuffer, snapshotBuffer);
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
