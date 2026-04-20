using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private readonly record struct MassSellInventoryState(
        Dictionary<string, int> StackTypeCounts,
        Dictionary<string, int> ProtoCounts)
    {
        public bool IsEmpty => StackTypeCounts.Count == 0 && ProtoCounts.Count == 0;
    }

    private bool _inComputeMassSellPlan;

    private readonly List<EntityUid> _massSellItemsScratch = new();
    private readonly List<NcStoreListingDef> _sellListingsScratch = new();

    public MassSellPlan ComputeMassSellPlan(NcStoreComponent store, EntityUid container)
    {
        _inventory.InvalidateInventoryCache(container);

        if (_inComputeMassSellPlan)
        {
            var localItems = new List<EntityUid>(64);
            _inventory.ScanInventoryItems(container, localItems);
            return ComputeMassSellPlanInternal(store, localItems);
        }

        _inventory.ScanInventoryItems(container, _massSellItemsScratch);
        return ComputeMassSellPlanInternal(store, _massSellItemsScratch);
    }

    public MassSellPlan ComputeMassSellPlanFromCachedItems(
        NcStoreComponent store,
        EntityUid container,
        IReadOnlyList<EntityUid> cachedItems
    ) =>
        ComputeMassSellPlanInternal(store, cachedItems);

    public Dictionary<string, int> GetMassSellValue(NcStoreComponent store, EntityUid container) =>
        ComputeMassSellPlan(store, container).IncomeByCurrency;

    private MassSellPlan ComputeMassSellPlanInternal(NcStoreComponent store, IEnumerable<EntityUid> items)
    {
        if (_inComputeMassSellPlan)
        {
            Sawmill.Warning(
                $"[MassSell] Re-entrant ComputeMassSellPlan rejected for store {ToPrettyString(store.Owner)}. " +
                "Returning empty plan to avoid scratch corruption. Check event handlers in the call path.");
            return CreateEmptyMassSellPlan();
        }

        _inComputeMassSellPlan = true;
        try
        {
            var plan = CreateEmptyMassSellPlan();
            if (store.Listings.Count == 0)
                return plan;

            var inventory = BuildMassSellInventoryState(items);
            if (inventory.IsEmpty)
                return plan;

            var listingQuotes = BuildMassSellListingQuotes(store);
            PrepareMassSellListings(store, listingQuotes);
            if (_sellListingsScratch.Count == 0)
                return plan;

            ApplyMassSellListings(inventory, listingQuotes, plan);
            return plan;
        }
        finally
        {
            _inComputeMassSellPlan = false;
        }
    }

    private static MassSellPlan CreateEmptyMassSellPlan()
    {
        return new(
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, (string, int)>(StringComparer.Ordinal),
            new List<MassSellStep>());
    }

    private MassSellInventoryState BuildMassSellInventoryState(IEnumerable<EntityUid> items)
    {
        var stackTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var protoCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var ent in items)
        {
            if (!_ents.EntityExists(ent))
                continue;

            if (_ents.TryGetComponent(ent, out StackComponent? stack))
            {
                TrackMassSellStackEntity(ent, stack, stackTypeCounts);
                continue;
            }

            TrackMassSellPrototypeEntity(ent, protoCounts);
        }

        return new(stackTypeCounts, protoCounts);
    }

    private void TrackMassSellStackEntity(
        EntityUid ent,
        StackComponent stack,
        Dictionary<string, int> stackTypeCounts)
    {
        var count = Math.Max(stack.Count, 0);
        if (count > 0 && !string.IsNullOrWhiteSpace(stack.StackTypeId))
            AddMassSellCount(stackTypeCounts, stack.StackTypeId, count);
    }

    private void TrackMassSellPrototypeEntity(
        EntityUid ent,
        Dictionary<string, int> protoCounts)
    {
        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return;

        AddMassSellCount(protoCounts, proto.ID, 1);
    }

    private static void AddMassSellCount(Dictionary<string, int> counts, string key, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(key))
            return;

        if (!counts.TryAdd(key, amount))
            counts[key] += amount;
    }

    private Dictionary<string, (string CurrencyId, int UnitPrice)> BuildMassSellListingQuotes(NcStoreComponent store)
    {
        var listingQuotes = new Dictionary<string, (string CurrencyId, int UnitPrice)>(StringComparer.Ordinal);

        foreach (var listing in store.Listings)
        {
            if (listing.Mode != StoreMode.Sell)
                continue;

            if (TryPickCurrencyForSell(store, listing, out var currencyId, out var unitPrice) &&
                unitPrice > 0 &&
                !string.IsNullOrWhiteSpace(currencyId))
            {
                listingQuotes[listing.Id] = (currencyId, unitPrice);
            }
            else
            {
                listingQuotes[listing.Id] = (string.Empty, 0);
            }
        }

        return listingQuotes;
    }

    private void PrepareMassSellListings(
        NcStoreComponent store,
        Dictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes)
    {
        _sellListingsScratch.Clear();

        foreach (var listing in store.Listings)
        {
            if (listing.Mode != StoreMode.Sell || string.IsNullOrEmpty(listing.ProductEntity) || listing.RemainingCount == 0)
                continue;

            if (!listingQuotes.TryGetValue(listing.Id, out var quote) || quote.UnitPrice <= 0)
                continue;

            _sellListingsScratch.Add(listing);
        }

        _sellListingsScratch.Sort((left, right) => CompareMassSellListings(left, right, listingQuotes));
    }

    private int CompareMassSellListings(
        NcStoreListingDef left,
        NcStoreListingDef right,
        Dictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes)
    {
        var leftPrice = listingQuotes[left.Id].UnitPrice;
        var rightPrice = listingQuotes[right.Id].UnitPrice;

        var priceCmp = rightPrice.CompareTo(leftPrice);
        if (priceCmp != 0)
            return priceCmp;

        var productCmp = OrdinalIds.Compare(left.ProductEntity, right.ProductEntity);
        if (productCmp != 0)
            return productCmp;

        return OrdinalIds.Compare(left.Id, right.Id);
    }

    private void ApplyMassSellListings(
        MassSellInventoryState inventory,
        Dictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes,
        MassSellPlan plan)
    {
        var stackComponentName = _compFactory.GetComponentName(typeof(StackComponent));

        foreach (var listing in _sellListingsScratch)
        {
            if (!listingQuotes.TryGetValue(listing.Id, out var quote) ||
                quote.UnitPrice <= 0 ||
                string.IsNullOrWhiteSpace(quote.CurrencyId))
            {
                continue;
            }

            var taken = ComputeMassSellListingTake(
                listing,
                quote.UnitPrice,
                stackComponentName,
                inventory);
            if (taken <= 0)
                continue;

            RecordMassSellStep(plan, listing, quote, taken);
        }
    }

    private int ComputeMassSellListingTake(
        NcStoreListingDef listing,
        int unitPrice,
        string stackComponentName,
        MassSellInventoryState inventory)
    {
        if (!TryComputeMassSellWantedUnits(listing.RemainingCount, unitPrice, out var want))
            return 0;

        var expectedStackType = TryGetMassSellExpectedStackType(listing.ProductEntity, stackComponentName);
        if (!string.IsNullOrEmpty(expectedStackType))
            return ReserveMassSellStackUnits(expectedStackType, want, inventory.StackTypeCounts);

        _ = listing.MatchMode;
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

    private string? TryGetMassSellExpectedStackType(string productEntity, string stackComponentName)
    {
        if (_protos.TryIndex<EntityPrototype>(productEntity, out var productProto) &&
            productProto.TryGetComponent(stackComponentName, out StackComponent? productStack))
        {
            return productStack.StackTypeId;
        }

        return null;
    }

    private static int ReserveMassSellStackUnits(
        string stackTypeId,
        int want,
        Dictionary<string, int> stackTypeCounts)
    {
        return ReserveMassSellUnits(stackTypeCounts, stackTypeId, want);
    }

    private static int ReserveMassSellProtoUnits(
        string protoId,
        int want,
        Dictionary<string, int> protoCounts)
    {
        return ReserveMassSellUnits(protoCounts, protoId, want);
    }

    private static int ReserveMassSellUnits(
        Dictionary<string, int> counts,
        string key,
        int want)
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
        int taken)
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
