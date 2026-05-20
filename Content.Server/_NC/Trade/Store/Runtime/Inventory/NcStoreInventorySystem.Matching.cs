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

public sealed partial class NcStoreInventorySystem
{
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

        if (matchMode == PrototypeMatchMode.Tag)
        {
            if (!TryResolveTradeTagId(productProtoId, out var tagId))
                return 0;

            var total = 0;
            foreach (var (protoId, count) in snapshot.ProtoCounts)
            {
                if (count <= 0)
                    continue;

                if (PrototypeHasTag(protoId, tagId))
                    total += count;
            }

            return total;
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
        var matcher = GetCompiledItemGroupMatcher(group);
        if (matcher == null)
            return false;

        if (matcher.Items.Contains(protoId))
            return true;

        if (_ents.TryGetComponent(entity, out StackComponent? stack) &&
            MatcherMatchesStackType(matcher, stack.StackTypeId))
        {
            return true;
        }

        return false;
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
}
