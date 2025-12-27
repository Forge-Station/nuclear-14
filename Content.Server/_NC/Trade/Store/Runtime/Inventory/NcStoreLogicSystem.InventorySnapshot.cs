using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public int GetBalance(EntityUid user, string stackType)
    {
        var total = 0;

        var cached = GetOrBuildDeepItemsCacheCompacted(user);

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

    private int GetOwnedInternal(EntityUid root, string productProtoId, PrototypeMatchMode matchMode)
    {
        var total = 0;

        var expectedStackType = GetProductStackType(productProtoId);
        var effective = ResolveMatchMode(productProtoId, matchMode);

        var cached = GetOrBuildDeepItemsCacheCompacted(root);

        foreach (var ent in cached)
        {
            if (IsProtectedFromDirectSale(root, ent))
                continue;

            if (expectedStackType != null &&
                _ents.TryGetComponent(ent, out StackComponent? stack) &&
                stack.StackTypeId == expectedStackType)
            {
                total += Math.Max(stack.Count, 0);
                continue;
            }

            if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is null)
                continue;

            if (effective == PrototypeMatchMode.Descendants)
            {
                if (IsProtoOrDescendant(meta.EntityPrototype, productProtoId))
                    total += 1;
            }
            else
            {
                if (meta.EntityPrototype.ID == productProtoId)
                    total += 1;
            }
        }

        return total;
    }


    public int GetOwned(EntityUid user, string productProtoId) =>
        GetOwnedInternal(user, productProtoId, PrototypeMatchMode.Exact);

    public int GetOwned(EntityUid user, string productProtoId, PrototypeMatchMode matchMode) =>
        GetOwnedInternal(user, productProtoId, matchMode);

    public int GetOwnedInRoot(EntityUid root, string productProtoId) =>
        GetOwnedInternal(root, productProtoId, PrototypeMatchMode.Exact);

    public int GetOwnedInRoot(EntityUid root, string productProtoId, PrototypeMatchMode matchMode) =>
        GetOwnedInternal(root, productProtoId, matchMode);

}
