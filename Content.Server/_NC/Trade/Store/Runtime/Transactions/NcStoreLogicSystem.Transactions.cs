using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    public bool TryBuy(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (!TryPrepareBuy(
            listingId,
            store,
            user,
            count,
            out var listing,
            out var effectiveProtoId,
            out _,
            out var plan))
            return false;

        var rewards = _transactionCoordinator.BuildSingleReward(
            StoreRewardType.Item,
            effectiveProtoId,
            plan.TotalUnits);
        if (!TryExecuteRewardListWithPreCommit(
            user,
            rewards,
            "Buy",
            () => TryTakeCurrency(user, plan.Currency, plan.TotalPrice)
                ? null
                : $"failed to take currency '{plan.Currency}' x{plan.TotalPrice}",
            out var reason))
        {
            Sawmill.Warning($"[NcStore] TryBuy failed for listing '{listing.Id}': {reason}");
            return false;
        }

        _inventory.InvalidateInventoryCache(user);

        ApplyDeliveredBuyPurchases(listing, plan.Purchases);
        LogSuccessfulBuy(listing, plan, plan.TotalUnits, plan.Purchases);
        return true;
    }

    private bool TryPrepareBuy(
        string listingId,
        NcStoreComponent? store,
        EntityUid user,
        int count,
        out NcStoreListingDef listing,
        out string effectiveProtoId,
        out EntityPrototype proto,
        out BuyExecutionPlan plan
    )
    {
        listing = default!;
        effectiveProtoId = string.Empty;
        proto = default!;
        plan = default;

        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Buy, listingId),
            out var foundListing))
            return false;

        // Phase M2: for Matcher listings, ProductEntity is an NcMatcherPrototype id. Resolve
        // it to a random concrete prototype from matcher.Items. For Exact listings, use the
        // ProductEntity directly as the prototype ID.
        if (!TryResolveBuyEffectiveProto(foundListing, out effectiveProtoId, out var foundProto) ||
            foundProto == null)
            return false;

        listing = foundListing;
        proto = foundProto;

        _inventory.InvalidateInventoryCache(user);
        var snapshot = _inventory.BuildInventorySnapshot(user);

        if (!TryPickCurrencyForBuy(store, listing, snapshot, out var currency, out var unitPrice, out var balance))
            return false;

        return TryBuildBuyPlan(currency, unitPrice, balance, count, listing, out plan);
    }

    /// <summary>
    ///     Phase M2: resolve a Buy listing to the concrete EntityPrototype that will be spawned.
    ///     For Exact match — the listing's ProductEntity is the prototype id directly.
    ///     For Matcher match — the listing's ProductEntity is an NcMatcherPrototype id; we load
    ///     the matcher, pick a random item from its Items list, and that becomes the effective prototype.
    ///     Returns false (and logs a warning) if:
    ///     - Exact: prototype doesn't exist.
    ///     - Matcher: matcher prototype doesn't exist, matcher has no items, or the randomly
    ///     picked item prototype doesn't exist.
    /// </summary>
    private bool TryResolveBuyEffectiveProto(
        NcStoreListingDef listing,
        out string effectiveProtoId,
        out EntityPrototype? proto
    )
    {
        effectiveProtoId = string.Empty;
        proto = null;

        if (listing.MatchMode == PrototypeMatchMode.Matcher)
        {
            if (!_protos.TryIndex<NcMatcherPrototype>(listing.ProductEntity, out var matcher))
            {
                Sawmill.Warning(
                    $"[NcStore] Buy prepare failed: matcher '{listing.ProductEntity}' not found for listing '{listing.Id}'.");
                return false;
            }

            if (matcher.Items is not { Count: > 0, })
            {
                Sawmill.Warning(
                    $"[NcStore] Buy prepare failed: matcher '{listing.ProductEntity}' has no items to pick from.");
                return false;
            }

            effectiveProtoId = _random.Pick(matcher.Items);
        }
        else
            effectiveProtoId = listing.ProductEntity;

        if (!_protos.TryIndex(effectiveProtoId, out proto))
        {
            Sawmill.Warning(
                $"[NcStore] Buy prepare failed: resolved prototype '{effectiveProtoId}' not found (listing '{listing.Id}').");
            return false;
        }

        return true;
    }

    private static bool TryBuildBuyPlan(
        string currency,
        int unitPrice,
        int balance,
        int requestedCount,
        NcStoreListingDef listing,
        out BuyExecutionPlan plan
    )
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
        out int totalUnits
    )
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

    private static void ApplyDeliveredBuyPurchases(NcStoreListingDef listing, int deliveredPurchases)
    {
        if (listing.RemainingCount >= 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - deliveredPurchases);
    }

    private void LogSuccessfulBuy(
        NcStoreListingDef listing,
        BuyExecutionPlan plan,
        int spawnedUnits,
        int deliveredPurchases
    ) =>
        Sawmill.Info(
            $"TryBuy: OK {listing.ProductEntity} x{spawnedUnits} ({deliveredPurchases} purchases) for {plan.UnitPrice} {plan.Currency} each");

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

        var owned = listing.MatchMode == PrototypeMatchMode.Matcher
            ? _inventory.GetOwnedFromRootCached(root, listing.ProductEntity, listing.MatchMode)
            : _inventory.GetOwnedFromSnapshot(
                _inventory.BuildInventorySnapshot(root),
                listing.ProductEntity,
                listing.MatchMode);

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

        if (!CanGiveCurrency(user, currency, (int) totalL))
        {
            Sawmill.Error(
                $"TrySell: payout currency '{currency}' cannot be issued to {ToPrettyString(user)}; " +
                $"refusing to consume '{listing.ProductEntity}'.");
            return false;
        }

        var rewards = _transactionCoordinator.BuildSingleReward(StoreRewardType.Currency, currency, (int) totalL);
        if (!TryExecuteRewardListWithPreCommit(
            user,
            rewards,
            "Sell",
            () => TryCommitSellTake(root, listing, actual),
            out var reason))
        {
            Sawmill.Warning($"[NcStore] TrySell failed for listing '{listing.Id}': {reason}");
            return false;
        }

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

    private string? TryCommitSellTake(EntityUid root, NcStoreListingDef listing, int amount) =>
        _transactionCoordinator.TryCommitInventoryTake(
            "Sell",
            () =>
            {
                if (!_inventory.TryTakeProductUnitsFromRootCached(
                    root,
                    listing.ProductEntity,
                    amount,
                    listing.MatchMode))
                    return $"failed to consume sold product '{listing.ProductEntity}' x{amount}";

                return null;
            });

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

    private readonly record struct BuyExecutionPlan(
        string Currency,
        int UnitPrice,
        int Purchases,
        int TotalPrice,
        int TotalUnits,
        int UnitsPerPurchase);
}
