using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public int GetBalance(EntityUid user, string stackType)
    {
        var total = 0;

        var cached = GetOrBuildDeepItemsCache(user);
        CompactCachedItems(cached);

        foreach (var entity in cached)
        {
            if (!_ents.EntityExists(entity))
                continue;
            if (_ents.TryGetComponent(entity, out StackComponent? stack) &&
                stack.StackTypeId == stackType)
            {
                total += stack.Count;
            }
        }

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
        var items = GetOrBuildDeepItemsCache(root);
        FillInventorySnapshotFromItems(root, items, buffer);
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
