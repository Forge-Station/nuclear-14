using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private void ApplyMassSellListings(
        MassSellInventoryState inventory,
        IReadOnlyDictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes,
        IReadOnlyList<NcStoreListingDef> sellListings,
        MassSellPlan plan
    )
    {
        foreach (var listing in sellListings)
        {
            if (!listingQuotes.TryGetValue(listing.Id, out var quote) ||
                quote.UnitPrice <= 0 ||
                string.IsNullOrWhiteSpace(quote.CurrencyId))
                continue;

            var taken = ComputeMassSellListingTake(
                listing,
                quote.UnitPrice,
                inventory);
            if (taken <= 0)
                continue;

            RecordMassSellStep(plan, listing, quote, taken);
        }
    }

    private int ComputeMassSellListingTake(
        NcStoreListingDef listing,
        int unitPrice,
        MassSellInventoryState inventory
    )
    {
        if (!TryComputeMassSellWantedUnits(listing.RemainingCount, unitPrice, out var want))
            return 0;

        if (listing.MatchMode == PrototypeMatchMode.Matcher)
            return ReserveMassSellMatcherUnits(listing.ProductEntity, want, inventory);

        if (listing.MatchMode == PrototypeMatchMode.Tag)
            return ReserveMassSellTagUnits(listing.ProductEntity, want, inventory);

        var expectedStackType = _inventory.GetProductStackType(listing.ProductEntity);
        if (!string.IsNullOrEmpty(expectedStackType))
            return ReserveMassSellStackUnits(expectedStackType, want, inventory);

        return ReserveMassSellProtoUnits(listing.ProductEntity, want, inventory.ProtoCounts);
    }

    private static bool TryComputeMassSellWantedUnits(int remainingCount, int unitPrice, out int want)
    {
        var remaining = remainingCount < -1 ? -1 : remainingCount;
        var maxByRemaining = remaining >= 0 ? remaining : int.MaxValue;
        var maxTakeByInt = unitPrice > 0 ? int.MaxValue / unitPrice : 0;
        want = maxByRemaining > 0 && maxTakeByInt > 0
            ? Math.Min(maxByRemaining, maxTakeByInt)
            : 0;
        return want > 0;
    }

    private int ReserveMassSellStackUnits(
        string stackTypeId,
        int want,
        MassSellInventoryState inventory
    )
    {
        var taken = ReserveMassSellUnits(inventory.StackTypeCounts, stackTypeId, want);
        if (taken <= 0)
            return 0;

        if (!inventory.StackTypeProtoCounts.TryGetValue(stackTypeId, out var perProto) || perProto.Count == 0)
            return taken;

        var left = taken;
        var protoIds = _massSellProtoIdsScratch;
        protoIds.Clear();
        foreach (var protoId in perProto.Keys)
            protoIds.Add(protoId);

        protoIds.Sort(StringComparer.Ordinal);

        foreach (var protoId in protoIds)
        {
            if (left <= 0)
                break;

            if (!perProto.TryGetValue(protoId, out var available) || available <= 0)
                continue;

            var take = Math.Min(available, left);
            perProto[protoId] = available - take;

            if (inventory.ProtoCounts.TryGetValue(protoId, out var protoAvailable) && protoAvailable > 0)
                inventory.ProtoCounts[protoId] = Math.Max(0, protoAvailable - take);

            left -= take;
        }

        var actualTaken = taken - left;
        if (actualTaken < taken)
            inventory.StackTypeCounts[stackTypeId] += taken - actualTaken;

        protoIds.Clear();
        return actualTaken;
    }

    private static int ReserveMassSellProtoUnits(
        string protoId,
        int want,
        Dictionary<string, int> protoCounts
    ) =>
        ReserveMassSellUnits(protoCounts, protoId, want);

    private int ReserveMassSellMatcherUnits(
        string matcherId,
        int want,
        MassSellInventoryState inventory
    )
    {
        if (want <= 0)
            return 0;

        var takenTotal = 0;
        var matchingStackTypeIds = _massSellMatchingStackTypeIdsScratch;
        _inventory.FillMatchingStackTypeIdsForMatcher(matcherId, inventory.StackTypeCounts, matchingStackTypeIds);

        foreach (var stackTypeId in matchingStackTypeIds)
        {
            if (takenTotal >= want)
                break;

            var left = want - takenTotal;
            takenTotal += ReserveMassSellStackUnits(stackTypeId, left, inventory);
        }

        matchingStackTypeIds.Clear();

        var matchingProtoIds = _massSellMatchingProtoIdsScratch;
        _inventory.FillMatchingPrototypeIdsForMatcher(matcherId, inventory.ProtoCounts, matchingProtoIds);

        if (matchingProtoIds.Count == 0)
            return takenTotal;

        foreach (var protoId in matchingProtoIds)
        {
            if (takenTotal >= want)
                break;

            var left = want - takenTotal;
            takenTotal += ReserveMassSellProtoUnits(protoId, left, inventory.ProtoCounts);
        }

        matchingProtoIds.Clear();
        return takenTotal;
    }

    private int ReserveMassSellTagUnits(
        string tagTargetId,
        int want,
        MassSellInventoryState inventory
    )
    {
        if (want <= 0)
            return 0;

        var takenTotal = 0;
        var matchingProtoIds = _massSellMatchingProtoIdsScratch;
        _inventory.FillMatchingPrototypeIdsForTag(tagTargetId, inventory.ProtoCounts, matchingProtoIds);

        foreach (var protoId in matchingProtoIds)
        {
            if (takenTotal >= want)
                break;

            var left = want - takenTotal;
            takenTotal += ReserveMassSellProtoUnits(protoId, left, inventory.ProtoCounts);
        }

        matchingProtoIds.Clear();
        return takenTotal;
    }

    private static int ReserveMassSellUnits(
        Dictionary<string, int> counts,
        string key,
        int want
    )
    {
        if (want <= 0 || !counts.TryGetValue(key, out var available) || available <= 0)
            return 0;

        var taken = Math.Min(available, want);
        counts[key] = available - taken;
        return taken;
    }

    private static void RecordMassSellStep(
        MassSellPlan plan,
        NcStoreListingDef listing,
        (string CurrencyId, int UnitPrice) quote,
        int taken
    )
    {
        var total = (long) quote.UnitPrice * taken;
        SafeAddIncome(plan.IncomeByCurrency, quote.CurrencyId, total);
        plan.UnitsByListingId[listing.Id] = taken;
        plan.PriceByListingId[listing.Id] = quote;
        plan.Steps.Add(new(listing, quote.CurrencyId, quote.UnitPrice, taken));
    }

    private static void SafeAddIncome(Dictionary<string, int> income, string currencyId, long delta)
    {
        if (delta <= 0)
            return;
        if (!income.TryGetValue(currencyId, out var cur))
            cur = 0;
        var sum = cur + delta;
        income[currencyId] = sum >= int.MaxValue ? int.MaxValue : (int) sum;
    }

    public readonly record struct MassSellStep(
        NcStoreListingDef Listing,
        string CurrencyId,
        int UnitPrice,
        int Count);

    public readonly record struct MassSellPlan(
        Dictionary<string, int> IncomeByCurrency,
        Dictionary<string, int> UnitsByListingId,
        Dictionary<string, (string CurrencyId, int UnitPrice)> PriceByListingId,
        List<MassSellStep> Steps);
}
