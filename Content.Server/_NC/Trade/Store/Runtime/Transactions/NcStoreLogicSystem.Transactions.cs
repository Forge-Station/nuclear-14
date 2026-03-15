using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private readonly record struct BuyExecutionPlan(
        string Currency,
        int UnitPrice,
        int Purchases,
        int TotalPrice,
        int TotalUnits,
        int UnitsPerPurchase);

    public bool TryBuy(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (!TryPrepareBuy(listingId, store, user, count, out var listing, out var proto, out var plan))
            return false;

        if (!TryTakeCurrency(user, plan.Currency, plan.TotalPrice))
            return false;

        var spawnedUnits = SpawnPurchasedProduct(
            user,
            listing.ProductEntity,
            proto,
            plan.Purchases,
            plan.UnitsPerPurchase);
        _inventory.InvalidateInventoryCache(user);

        return FinalizeBuy(user, listing, plan, spawnedUnits);
    }

    private bool TryPrepareBuy(
        string listingId,
        NcStoreComponent? store,
        EntityUid user,
        int count,
        out NcStoreListingDef listing,
        out EntityPrototype proto,
        out BuyExecutionPlan plan)
    {
        listing = default!;
        proto = default!;
        plan = default;

        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
                NcStoreComponent.MakeListingKey(StoreMode.Buy, listingId),
                out NcStoreListingDef? foundListing))
            return false;

        if (!_protos.TryIndex<EntityPrototype>(foundListing.ProductEntity, out EntityPrototype? foundProto))
            return false;

        listing = foundListing;
        proto = foundProto;

        _inventory.InvalidateInventoryCache(user);
        var snapshot = _inventory.BuildInventorySnapshot(user);

        if (!TryPickCurrencyForBuy(store, listing, snapshot, out var currency, out var unitPrice, out var balance))
            return false;

        return TryBuildBuyPlan(currency, unitPrice, balance, count, listing, out plan);
    }

    private static bool TryBuildBuyPlan(
        string currency,
        int unitPrice,
        int balance,
        int requestedCount,
        NcStoreListingDef listing,
        out BuyExecutionPlan plan)
    {
        plan = default;

        var unitsPerPurchase = Math.Max(1, listing.UnitsPerPurchase);
        var maxByRemainingPurchases = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;
        var maxByMoneyPurchases = unitPrice > 0 ? balance / unitPrice : int.MaxValue;
        var maxPurchases = Math.Min(maxByRemainingPurchases, maxByMoneyPurchases);
        if (maxPurchases <= 0)
            return false;

        var purchases = Math.Min(requestedCount, maxPurchases);
        if (!TryComputeBuyTotals(unitPrice, purchases, unitsPerPurchase, out var totalPrice, out var totalUnits))
            return false;

        plan = new(currency, unitPrice, purchases, totalPrice, totalUnits, unitsPerPurchase);
        return true;
    }

    private static bool TryComputeBuyTotals(
        int unitPrice,
        int purchases,
        int unitsPerPurchase,
        out int totalPrice,
        out int totalUnits)
    {
        totalPrice = 0;
        totalUnits = 0;

        var totalPriceLong = (long) unitPrice * purchases;
        if (totalPriceLong > int.MaxValue)
            return false;

        var totalUnitsLong = (long) purchases * unitsPerPurchase;
        if (totalUnitsLong <= 0 || totalUnitsLong > int.MaxValue)
            return false;

        totalPrice = (int) totalPriceLong;
        totalUnits = (int) totalUnitsLong;
        return true;
    }

    private bool FinalizeBuy(
        EntityUid user,
        NcStoreListingDef listing,
        BuyExecutionPlan plan,
        int spawnedUnits)
    {
        if (spawnedUnits <= 0)
            return RefundFailedBuy(user, plan);

        var deliveredPurchases = spawnedUnits / plan.UnitsPerPurchase;
        if (deliveredPurchases <= 0)
            return RefundFailedBuy(user, plan);

        RefundUndeliveredBuyPurchases(user, plan, deliveredPurchases);
        ApplyDeliveredBuyPurchases(listing, deliveredPurchases);
        LogSuccessfulBuy(listing, plan, spawnedUnits, deliveredPurchases);
        return true;
    }

    private bool RefundFailedBuy(EntityUid user, BuyExecutionPlan plan)
    {
        GiveCurrency(user, plan.Currency, plan.TotalPrice);
        return false;
    }

    private void RefundUndeliveredBuyPurchases(EntityUid user, BuyExecutionPlan plan, int deliveredPurchases)
    {
        if (deliveredPurchases >= plan.Purchases)
            return;

        var refundPurchases = plan.Purchases - deliveredPurchases;
        var refund = (long) refundPurchases * plan.UnitPrice;
        if (refund > 0 && refund <= int.MaxValue)
            GiveCurrency(user, plan.Currency, (int) refund);
    }

    private static void ApplyDeliveredBuyPurchases(NcStoreListingDef listing, int deliveredPurchases)
    {
        if (listing.RemainingCount >= 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - deliveredPurchases);
    }

    private void LogSuccessfulBuy(
        NcStoreListingDef listing,
        BuyExecutionPlan plan,
        int spawnedUnits,
        int deliveredPurchases)
    {
        Sawmill.Info(
            $"TryBuy: OK {listing.ProductEntity} x{spawnedUnits} ({deliveredPurchases} purchases) for {plan.UnitPrice} {plan.Currency} each");
    }

    public bool TrySell(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null)
            return false;
        return TrySellScenario(listingId, store, user, user, count, out _);
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
        if (store == null)
            return false;
        return TrySellScenario(listingId, store, user, container, count, out var sold) &&
            LogSellFromContainer(sold, listingId, store, container);
    }

    private bool TrySellScenario(
        string listingId,
        NcStoreComponent store,
        EntityUid user,
        EntityUid root,
        int count,
        out int sold
    )
    {
        sold = 0;
        if (store.Listings.Count == 0 || count <= 0)
            return false;
        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Sell, listingId),
            out var listing))
            return false;
        if (!TryPickCurrencyForSell(store, listing, out var currency, out var unitPrice) || unitPrice <= 0)
            return false;

        _inventory.InvalidateInventoryCache(root);

        var snap = _inventory.BuildInventorySnapshot(root);
        var owned = _inventory.GetOwnedFromSnapshot(snap, listing.ProductEntity, listing.MatchMode);

        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;
        var maxPossible = Math.Min(owned, maxByRemaining);
        if (maxPossible <= 0)
            return false;

        var maxByPayout = int.MaxValue / unitPrice;
        if (maxByPayout <= 0)
            return false;

        var actual = Math.Min(count, Math.Min(maxPossible, maxByPayout));
        if (actual <= 0)
            return false;

        var totalL = (long) unitPrice * actual;
        if (totalL > int.MaxValue)
            return false;

        var ok = _inventory.TryTakeProductUnitsFromRootCached(root, listing.ProductEntity, actual, listing.MatchMode);
        if (!ok)
            return false;

        GiveCurrency(user, currency, (int) totalL);

        _inventory.InvalidateInventoryCache(user);
        if (root != user)
            _inventory.InvalidateInventoryCache(root);

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - actual);
        sold = actual;

        if (root == user)
            Sawmill.Info($"TrySell: OK {listing.ProductEntity} x{actual} for {unitPrice} {currency} each");
        return true;
    }

    private bool LogSellFromContainer(int sold, string listingId, NcStoreComponent store, EntityUid container)
    {
        if (sold <= 0)
            return false;
        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Sell, listingId),
            out var listing))
            return true;
        if (!TryPickCurrencyForSell(store, listing, out var currency, out var unitPrice) || unitPrice <= 0)
            return true;
        Sawmill.Info(
            $"TrySellFromContainer: OK {listing.ProductEntity} x{sold} for {unitPrice} {currency} each (container={ToPrettyString(container)})");
        return true;
    }

    public bool TryExchange(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user)
    {
        if (store == null || store.Listings.Count == 0)
            return false;
        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Exchange, listingId),
            out var listing))
            return false;
        if (string.IsNullOrEmpty(listing.ProductEntity))
            return false;

        var requiredCount = listing.RemainingCount > 0 ? listing.RemainingCount : 1;
        if (requiredCount <= 0)
            return false;

        _inventory.InvalidateInventoryCache(user);

        var snap = _inventory.BuildInventorySnapshot(user);
        var owned = _inventory.GetOwnedFromSnapshot(snap, listing.ProductEntity, listing.MatchMode);

        if (owned < requiredCount)
            return false;

        if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var rewardPerUnit) || rewardPerUnit <= 0)
            return false;

        var totalRewardL = (long) rewardPerUnit * requiredCount;
        if (totalRewardL > int.MaxValue)
            return false;

        if (!_inventory.TryTakeProductUnitsFromRootCached(
            user,
            listing.ProductEntity,
            requiredCount,
            listing.MatchMode))
            return false;

        GiveCurrency(user, currencyId, (int) totalRewardL);
        _inventory.InvalidateInventoryCache(user);
        listing.RemainingCount = 0;
        return true;
    }
}
