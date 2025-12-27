using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public bool TryBuy(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(NcStoreComponent.MakeListingKey(StoreMode.Buy, listingId), out var listing))
            return false;

        if (!_protos.TryIndex<EntityPrototype>(listing.ProductEntity, out var proto))
            return false;

        InvalidateInventoryCache(user);
        var snap = BuildInventorySnapshot(user);

        if (!TryPickCurrencyForBuy(store, listing, snap, out var currency, out var unitPrice, out var balance))
            return false;

        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;
        var maxByMoney = unitPrice > 0 ? balance / unitPrice : int.MaxValue;

        var maxPossible = Math.Min(maxByRemaining, maxByMoney);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        var totalPriceL = (long) unitPrice * actual;
        if (totalPriceL > int.MaxValue)
            return false;

        var totalPrice = (int) totalPriceL;
        if (!TryTakeCurrency(user, currency, totalPrice))
            return false;

        var spawnedTotal = 0;
        var userCoords = _ents.GetComponent<TransformComponent>(user).Coordinates;

        if (proto.TryGetComponent("Stack", out StackComponent? stackComp))
        {
            var maxPerStack = int.MaxValue;
            if (_protos.TryIndex<StackPrototype>(stackComp.StackTypeId, out var stackTypeProto))
                maxPerStack = stackTypeProto.MaxCount ?? int.MaxValue;

            if (maxPerStack <= 0)
                maxPerStack = 1;

            var remainingToSpawn = actual;

            while (remainingToSpawn > 0)
            {
                var amount = Math.Min(remainingToSpawn, maxPerStack);
                try
                {
                    var spawned = _ents.SpawnEntity(listing.ProductEntity, userCoords);
                    if (_ents.TryGetComponent(spawned, out StackComponent? spawnedStack))
                        _stacks.SetCount(spawned, amount, spawnedStack);

                    var pickedUp = false;
                    if (_ents.HasComponent<HandsComponent>(user))
                        pickedUp = _hands.TryPickupAnyHand(user, spawned, false);

                    if (!pickedUp && TryGetPulledClosedCrate(user, out var crate) && Exists(crate))
                    {
                        _entityStorage.Insert(spawned, crate);
                        InvalidateInventoryCache(crate);
                    }

                    spawnedTotal += amount;
                    remainingToSpawn -= amount;
                }
                catch (Exception e)
                {
                    Sawmill.Error($"Spawn failed during bulk buy: {e}");

                    if (remainingToSpawn > 0)
                    {
                        var refundL = (long) remainingToSpawn * unitPrice;
                        if (refundL > 0 && refundL <= int.MaxValue)
                            GiveCurrency(user, currency, (int) refundL);
                    }

                    break;
                }
            }
        }
        else
        {
            for (var i = 0; i < actual; i++)
                if (TrySpawnProduct(listing.ProductEntity, user))
                    spawnedTotal++;
                else
                    GiveCurrency(user, currency, unitPrice);
        }

        InvalidateInventoryCache(user);

        if (spawnedTotal <= 0)
            return false;

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - spawnedTotal);

        Sawmill.Info($"TryBuy: OK {listing.ProductEntity} x{spawnedTotal} for {unitPrice} {currency} each");
        return true;
    }

    public bool TrySell(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Sell, listingId),
            out var listing))
            return false;


        if (!TryPickCurrencyForSell(store, listing, out var currency, out var unitPrice) || unitPrice <= 0)
            return false;

        InvalidateInventoryCache(user);
        var owned = GetOwned(user, listing.ProductEntity, listing.MatchMode);
        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;

        var maxPossible = Math.Min(owned, maxByRemaining);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        if (!TryTakeProductUnits(user, listing.ProductEntity, actual, listing.MatchMode))
            return false;

        var totalL = (long) unitPrice * actual;
        if (totalL > int.MaxValue)
            return false;

        GiveCurrency(user, currency, (int) totalL);
        InvalidateInventoryCache(user);

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - actual);

        Sawmill.Info($"TrySell: OK {listing.ProductEntity} x{actual} for {unitPrice} {currency} each");
        return true;
    }

    public bool TrySellFromContainer(
        string listingId,
        EntityUid machine,
        NcStoreComponent? store,
        EntityUid user,
        EntityUid container,
        int count = 1
    )
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Sell, listingId),
            out var listing))
            return false;

        if (!TryPickCurrencyForSell(store, listing, out var currency, out var unitPrice) || unitPrice <= 0)
            return false;

        InvalidateInventoryCache(container);

        var owned = GetOwnedInRoot(container, listing.ProductEntity, listing.MatchMode);
        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;

        var maxPossible = Math.Min(owned, maxByRemaining);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        if (!TryTakeProductUnitsFromRoot(container, listing.ProductEntity, actual, listing.MatchMode))
            return false;

        var totalL = (long) unitPrice * actual;
        if (totalL > int.MaxValue)
            return false;

        GiveCurrency(user, currency, (int) totalL);
        InvalidateInventoryCache(container);
        InvalidateInventoryCache(user);

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - actual);

        Sawmill.Info(
            $"TrySellFromContainer: OK {listing.ProductEntity} x{actual} for {unitPrice} {currency} each (container={ToPrettyString(container)})");
        return true;
    }


    private int GetOwnedInternal(EntityUid root, string productProtoId, PrototypeMatchMode matchMode)
    {
        var total = 0;

        var expectedStackType = GetProductStackType(productProtoId);
        var effective = ResolveMatchMode(productProtoId, matchMode);

        var cached = GetOrBuildDeepItemsCache(root);
        CompactCachedItems(cached);

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




    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount) =>
        TryTakeProductUnitsFromRootCached(user, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsFromRootCached(user, protoId, amount, matchMode);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount) =>
        TryTakeProductUnitsFromRootCached(root, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsFromRootCached(root, protoId, amount, matchMode);

    public bool TryExchange(
        string listingId,
        EntityUid machine,
        NcStoreComponent? store,
        EntityUid user
    )
    {
        if (store == null || store.Listings.Count == 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Exchange, listingId),
            out var listing))
            return false;

        if (string.IsNullOrEmpty(listing.ProductEntity))
            return false;

        var requiredCount = listing.RemainingCount > 0
            ? listing.RemainingCount
            : 1;

        if (requiredCount <= 0)
            return false;

        InvalidateInventoryCache(user);
        var owned = GetOwned(user, listing.ProductEntity, listing.MatchMode);
        if (owned < requiredCount)
            return false;

        if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var rewardPerUnit) ||
            rewardPerUnit <= 0)
            return false;

        if (!TryTakeProductUnits(user, listing.ProductEntity, requiredCount, listing.MatchMode))
            return false;

        var totalRewardL = (long) rewardPerUnit * requiredCount;
        if (totalRewardL > int.MaxValue)
            return false;

        GiveCurrency(user, currencyId, (int) totalRewardL);

        InvalidateInventoryCache(user);

        listing.RemainingCount = 0;

        return true;
    }





    private int GetInheritanceDepth(string protoId)
    {
        if (_inheritanceDepthCache.TryGetValue(protoId, out var depth))
            return depth;

        if (!_protos.TryIndex<EntityPrototype>(protoId, out var proto))
        {
            _inheritanceDepthCache[protoId] = 0;
            return 0;
        }

        var max = 0;
        if (proto.Parents != null)
        {
            foreach (var parent in proto.Parents)
            {
                var d = GetInheritanceDepth(parent) + 1;
                if (d > max)
                    max = d;
            }
        }

        _inheritanceDepthCache[protoId] = max;
        return max;
    }

    private static void SafeAddIncome(Dictionary<string, int> income, string currencyId, long delta)
    {
        if (delta <= 0)
            return;

        if (!income.TryGetValue(currencyId, out var cur))
            cur = 0;

        var sum = cur + delta;
        if (sum >= int.MaxValue)
            income[currencyId] = int.MaxValue;
        else
            income[currencyId] = (int) sum;
    }

}
