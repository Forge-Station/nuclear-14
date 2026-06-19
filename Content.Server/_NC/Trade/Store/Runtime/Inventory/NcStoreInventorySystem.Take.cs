using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreInventorySystem
{
    public bool TryTakeReservedEntityUnitsFromRoot(EntityUid root, EntityUid ent, int amount)
    {
        if (amount <= 0 || ShouldSkipTakeEntity(root, ent))
            return false;

        if (_ents.TryGetComponent(ent, out StackComponent? stack))
        {
            var have = Math.Max(stack.Count, 0);
            if (have < amount)
                return false;

            TrackTakeTransactionStackRestore(ent, stack.Count);
            _stacks.SetCount(ent, have - amount, stack);
            if (stack.Count <= 0)
                DeleteConsumedEntity(ent);

            if (!_takeTransactionActive)
                InvalidateInventoryCache(root);

            return true;
        }

        if (amount != 1)
            return false;

        DeleteConsumedEntity(ent);
        if (!_takeTransactionActive)
            InvalidateInventoryCache(root);

        return true;
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
            var matcher = GetCompiledMatcher(protoId, true);
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

        if (matchMode == PrototypeMatchMode.Tag)
        {
            if (!TryResolveTradeTagId(protoId, out var tagId))
            {
                return new(
                    protoId,
                    null,
                    matchMode,
                    null,
                    false);
            }

            return new(
                tagId,
                null,
                matchMode,
                null,
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
        int maxNeeded
    )
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
        int amount
    )
    {
        var left = amount;
        var compactNeeded = false;

        for (var i = 0; i < cachedItems.Count && left > 0; i++)
            if (!TryConsumeTakeUnitsFromEntity(root, cachedItems, i, request, ref left, ref compactNeeded))
                continue;

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
        ref bool compactNeeded
    )
    {
        var ent = cachedItems[index];
        if (ShouldSkipTakeEntity(root, ent))
            return false;

        if (request.StackType != null)
            return TryConsumeStackTypeTake(cachedItems, index, ent, request.StackType, ref left, ref compactNeeded);

        return TryConsumePrototypeTake(cachedItems, index, ent, request, ref left, ref compactNeeded);
    }

    private bool ShouldSkipTakeEntity(EntityUid root, EntityUid ent) =>
        ent == EntityUid.Invalid || !_ents.EntityExists(ent) || IsProtectedFromDirectSale(root, ent);

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
        ref bool compactNeeded
    )
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
        ref bool compactNeeded
    )
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
                return true;

            return false;
        }

        if (request.MatchMode == PrototypeMatchMode.Tag)
            return PrototypeHasTag(proto.ID, request.ProtoId);

        return proto.ID == request.ProtoId;
    }

    private void ConsumeStackUnits(
        List<EntityUid> cachedItems,
        int index,
        EntityUid ent,
        StackComponent stack,
        ref int left,
        ref bool compactNeeded
    )
    {
        var have = Math.Max(stack.Count, 0);
        var take = Math.Min(have, left);
        TrackTakeTransactionStackRestore(ent, stack.Count);
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
        ref bool compactNeeded
    )
    {
        DeleteConsumedEntity(cachedItems, index, ent, ref compactNeeded);
        left -= 1;
    }

    private void DeleteConsumedEntity(
        List<EntityUid> cachedItems,
        int index,
        EntityUid ent,
        ref bool compactNeeded
    )
    {
        DeleteConsumedEntity(ent);

        cachedItems[index] = EntityUid.Invalid;
        compactNeeded = true;
    }

    private void DeleteConsumedEntity(EntityUid ent)
    {
        if (_takeTransactionActive)
            _takeTransactionDeleteScratch.Add(ent);
        else
            _ents.DeleteEntity(ent);
    }
}
