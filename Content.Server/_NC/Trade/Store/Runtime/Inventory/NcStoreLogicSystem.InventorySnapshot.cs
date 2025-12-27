using Content.Shared._NC.Trade;
using Content.Shared.Stacks;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    public int GetBalance(EntityUid user, string stackType)
    {
        var total = 0;
        foreach (var entity in EnumerateDeepItemsUnique(user))
            if (_ents.TryGetComponent(entity, out StackComponent? stack) &&
                stack.StackTypeId == stackType)
                total += stack.Count;

        return total;
    }

    public InventorySnapshot BuildInventorySnapshot(EntityUid root)
    {
        var snap = new InventorySnapshot();
        FillInventorySnapshot(root, snap);
        return snap;
    }

    public void FillInventorySnapshot(EntityUid root, InventorySnapshot buffer)
    {
        buffer.Clear();

        foreach (var ent in EnumerateDeepItemsUnique(root))
        {
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

    public int GetOwnedFromSnapshot(in InventorySnapshot snapshot, string productProtoId) =>
        GetOwnedFromSnapshot(snapshot, productProtoId, PrototypeMatchMode.Exact);

    public int GetOwnedFromSnapshot(in InventorySnapshot snapshot, string productProtoId, PrototypeMatchMode matchMode)
    {
        var stackType = GetProductStackType(productProtoId);
        if (stackType != null)
            return snapshot.StackTypeCounts.TryGetValue(stackType, out var cnt) ? cnt : 0;

        var effective = ResolveMatchMode(productProtoId, matchMode);

        if (effective == PrototypeMatchMode.Descendants)
            return snapshot.AncestorCounts.TryGetValue(productProtoId, out var units) ? units : 0;

        return snapshot.ProtoCounts.TryGetValue(productProtoId, out var exact) ? exact : 0;
    }

    public sealed class InventorySnapshot
    {
        public readonly Dictionary<string, int> AncestorCounts = new();
        public readonly Dictionary<string, int> ProtoCounts = new();
        public readonly Dictionary<string, int> StackTypeCounts = new();

        public void Clear()
        {
            ProtoCounts.Clear();
            AncestorCounts.Clear();
            StackTypeCounts.Clear();
        }
    }
}
