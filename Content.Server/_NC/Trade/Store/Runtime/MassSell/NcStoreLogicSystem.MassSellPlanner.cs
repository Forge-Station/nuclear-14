using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private sealed class MassSellCatalogCache
    {
        public readonly Dictionary<string, (string CurrencyId, int UnitPrice)> ListingQuotes = new(StringComparer.Ordinal);
        public readonly List<NcStoreListingDef> SellListings = new();
        public int CatalogRevision = int.MinValue;
    }

    private readonly record struct MassSellInventoryState(
        Dictionary<string, int> StackTypeCounts,
        Dictionary<string, int> ProtoCounts,
        Dictionary<string, Dictionary<string, int>> StackTypeProtoCounts)
    {
        public bool IsEmpty => StackTypeCounts.Count == 0 && ProtoCounts.Count == 0;
    }

    private readonly Dictionary<EntityUid, MassSellCatalogCache> _massSellCatalogCache = new();
    private readonly List<string> _massSellMatchingProtoIdsScratch = new();
    private readonly List<string> _massSellMatchingStackTypeIdsScratch = new();
    private readonly List<string> _massSellProtoIdsScratch = new();

    public MassSellPlan ComputeMassSellPlan(EntityUid storeUid, NcStoreComponent store, EntityUid container)
    {
        _inventory.InvalidateInventoryCache(container);

        var items = new List<EntityUid>(64);
        _inventory.ScanInventoryItems(container, items);
        return ComputeMassSellPlanInternal(storeUid, store, items);
    }

    public MassSellPlan ComputeMassSellPlanFromCachedItems(
        EntityUid storeUid,
        NcStoreComponent store,
        EntityUid container,
        IReadOnlyList<EntityUid> cachedItems
    ) =>
        ComputeMassSellPlanInternal(storeUid, store, cachedItems);

    public Dictionary<string, int> GetMassSellValue(EntityUid storeUid, NcStoreComponent store, EntityUid container) =>
        ComputeMassSellPlan(storeUid, store, container).IncomeByCurrency;

    public void ClearStoreRuntimeCaches(EntityUid store)
    {
        _massSellCatalogCache.Remove(store);
    }

    private MassSellPlan ComputeMassSellPlanInternal(EntityUid storeUid, NcStoreComponent store, IEnumerable<EntityUid> items)
    {
        var plan = CreateEmptyMassSellPlan();
        if (store.Listings.Count == 0)
            return plan;

        var inventory = BuildMassSellInventoryState(items);
        if (inventory.IsEmpty)
            return plan;

        var catalog = GetMassSellCatalogCache(storeUid, store);
        if (catalog.SellListings.Count == 0)
            return plan;

        ApplyMassSellListings(inventory, catalog.ListingQuotes, catalog.SellListings, plan);
        return plan;
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
        var stackTypeProtoCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var ent in items)
        {
            if (!_ents.EntityExists(ent))
                continue;

            if (_ents.TryGetComponent(ent, out StackComponent? stack))
            {
                TrackMassSellStackEntity(ent, stack, stackTypeCounts, protoCounts, stackTypeProtoCounts);
                continue;
            }

            TrackMassSellPrototypeEntity(ent, protoCounts, 1);
        }

        return new(stackTypeCounts, protoCounts, stackTypeProtoCounts);
    }

    private void TrackMassSellStackEntity(
        EntityUid ent,
        StackComponent stack,
        Dictionary<string, int> stackTypeCounts,
        Dictionary<string, int> protoCounts,
        Dictionary<string, Dictionary<string, int>> stackTypeProtoCounts)
    {
        var count = Math.Max(stack.Count, 0);
        if (count <= 0)
            return;

        var stackTypeId = stack.StackTypeId;
        if (!string.IsNullOrWhiteSpace(stackTypeId))
            AddMassSellCount(stackTypeCounts, stackTypeId, count);

        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return;

        AddMassSellCount(protoCounts, proto.ID, count);

        if (string.IsNullOrWhiteSpace(stackTypeId))
            return;

        if (!stackTypeProtoCounts.TryGetValue(stackTypeId, out var perProto))
        {
            perProto = new Dictionary<string, int>(StringComparer.Ordinal);
            stackTypeProtoCounts[stackTypeId] = perProto;
        }

        AddMassSellCount(perProto, proto.ID, count);
    }

    private void TrackMassSellPrototypeEntity(
        EntityUid ent,
        Dictionary<string, int> protoCounts,
        int amount)
    {
        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return;

        AddMassSellCount(protoCounts, proto.ID, amount);
    }

    private static void AddMassSellCount(Dictionary<string, int> counts, string key, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(key))
            return;

        if (!counts.TryAdd(key, amount))
            counts[key] += amount;
    }

    private MassSellCatalogCache GetMassSellCatalogCache(EntityUid storeUid, NcStoreComponent store)
    {
        if (!_massSellCatalogCache.TryGetValue(storeUid, out var cache))
        {
            cache = new MassSellCatalogCache();
            _massSellCatalogCache[storeUid] = cache;
        }

        if (cache.CatalogRevision == store.CatalogRevision)
            return cache;

        RebuildMassSellCatalogCache(store, cache);
        return cache;
    }

    private void RebuildMassSellCatalogCache(NcStoreComponent store, MassSellCatalogCache cache)
    {
        cache.ListingQuotes.Clear();
        cache.SellListings.Clear();

        foreach (var listing in store.Listings)
        {
            if (listing.Mode != StoreMode.Sell)
                continue;

            if (TryPickCurrencyForSell(store, listing, out var currencyId, out var unitPrice) &&
                unitPrice > 0 &&
                !string.IsNullOrWhiteSpace(currencyId))
            {
                cache.ListingQuotes[listing.Id] = (currencyId, unitPrice);
            }
            else
            {
                cache.ListingQuotes[listing.Id] = (string.Empty, 0);
            }
        }

        PrepareMassSellListings(store, cache.ListingQuotes, cache.SellListings);
        cache.CatalogRevision = store.CatalogRevision;
    }

    private void PrepareMassSellListings(
        NcStoreComponent store,
        IReadOnlyDictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes,
        List<NcStoreListingDef> sellListings)
    {
        sellListings.Clear();

        foreach (var listing in store.Listings)
        {
            if (listing.Mode != StoreMode.Sell || string.IsNullOrEmpty(listing.ProductEntity) || listing.RemainingCount == 0)
                continue;

            if (!listingQuotes.TryGetValue(listing.Id, out var quote) || quote.UnitPrice <= 0)
                continue;

            sellListings.Add(listing);
        }

        sellListings.Sort((left, right) => CompareMassSellListings(left, right, listingQuotes));
    }

    private int CompareMassSellListings(
        NcStoreListingDef left,
        NcStoreListingDef right,
        IReadOnlyDictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes)
    {
        var matchModeCmp = CompareMassSellMatchModePriority(left.MatchMode, right.MatchMode);
        if (matchModeCmp != 0)
            return matchModeCmp;

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

    private static int CompareMassSellMatchModePriority(PrototypeMatchMode left, PrototypeMatchMode right)
    {
        return GetMassSellMatchModePriority(left).CompareTo(GetMassSellMatchModePriority(right));
    }

    private static int GetMassSellMatchModePriority(PrototypeMatchMode mode)
    {
        return mode switch
        {
            PrototypeMatchMode.Exact => 0,
            PrototypeMatchMode.Matcher => 1,
            PrototypeMatchMode.Tag => 2,
            _ => 3
        };
    }

    private void ApplyMassSellListings(
        MassSellInventoryState inventory,
        IReadOnlyDictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes,
        IReadOnlyList<NcStoreListingDef> sellListings,
        MassSellPlan plan)
    {
        foreach (var listing in sellListings)
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
                inventory);
            if (taken <= 0)
                continue;

            RecordMassSellStep(plan, listing, quote, taken);
        }
    }

    private int ComputeMassSellListingTake(
        NcStoreListingDef listing,
        int unitPrice,
        MassSellInventoryState inventory)
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
        MassSellInventoryState inventory)
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
        Dictionary<string, int> protoCounts)
    {
        return ReserveMassSellUnits(protoCounts, protoId, want);
    }

    private int ReserveMassSellMatcherUnits(
        string matcherId,
        int want,
        MassSellInventoryState inventory)
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
        MassSellInventoryState inventory)
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
