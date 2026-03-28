using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private readonly record struct MassSellInventoryState(
        Dictionary<string, int> StackTypeCounts,
        Dictionary<string, int> ProtoCounts,
        Dictionary<string, EntityPrototype> ProtoCache,
        Dictionary<string, string> StackProtoToType)
    {
        public bool IsEmpty => StackTypeCounts.Count == 0 && ProtoCounts.Count == 0;
    }

    private readonly Dictionary<string, int> _inheritanceDepthCache = new(StringComparer.Ordinal);
    private readonly List<EntityUid> _massSellItemsScratch = new();
    private readonly List<string> _protoIdsScratch = new();
    private readonly HashSet<string> _protoIdsSeenScratch = new(StringComparer.Ordinal);
    private readonly List<NcStoreListingDef> _sellListingsScratch = new();

    public MassSellPlan ComputeMassSellPlan(NcStoreComponent store, EntityUid container)
    {
        _inventory.InvalidateInventoryCache(container);
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
        var plan = CreateEmptyMassSellPlan();
        if (store.Listings.Count == 0)
            return plan;

        var inventory = BuildMassSellInventoryState(items);
        if (inventory.IsEmpty)
            return plan;

        PrepareMassSellProtoIds(inventory);
        var listingQuotes = BuildMassSellListingQuotes(store);
        PrepareMassSellListings(store, listingQuotes);
        if (_sellListingsScratch.Count == 0)
            return plan;

        var matchesByExpected = BuildMassSellDescendantMatches(inventory.ProtoCache);
        ApplyMassSellListings(inventory, listingQuotes, matchesByExpected, plan);
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
        var protoCache = new Dictionary<string, EntityPrototype>(StringComparer.Ordinal);
        var stackProtoToType = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var ent in items)
        {
            if (!_ents.EntityExists(ent))
                continue;

            if (_ents.TryGetComponent(ent, out StackComponent? stack))
            {
                TrackMassSellStackEntity(ent, stack, stackTypeCounts, protoCache, stackProtoToType);
                continue;
            }

            TrackMassSellPrototypeEntity(ent, protoCounts, protoCache);
        }

        return new(stackTypeCounts, protoCounts, protoCache, stackProtoToType);
    }

    private void TrackMassSellStackEntity(
        EntityUid ent,
        StackComponent stack,
        Dictionary<string, int> stackTypeCounts,
        Dictionary<string, EntityPrototype> protoCache,
        Dictionary<string, string> stackProtoToType)
    {
        var count = Math.Max(stack.Count, 0);
        if (count > 0 && !string.IsNullOrWhiteSpace(stack.StackTypeId))
            AddMassSellCount(stackTypeCounts, stack.StackTypeId, count);

        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return;

        protoCache[proto.ID] = proto;
        if (!string.IsNullOrWhiteSpace(stack.StackTypeId))
            stackProtoToType.TryAdd(proto.ID, stack.StackTypeId);
    }

    private void TrackMassSellPrototypeEntity(
        EntityUid ent,
        Dictionary<string, int> protoCounts,
        Dictionary<string, EntityPrototype> protoCache)
    {
        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return;

        AddMassSellCount(protoCounts, proto.ID, 1);
        protoCache[proto.ID] = proto;
    }

    private static void AddMassSellCount(Dictionary<string, int> counts, string key, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(key))
            return;

        if (!counts.TryAdd(key, amount))
            counts[key] += amount;
    }

    private void PrepareMassSellProtoIds(MassSellInventoryState inventory)
    {
        _protoIdsSeenScratch.Clear();
        _protoIdsScratch.Clear();

        foreach (var protoId in inventory.ProtoCounts.Keys)
            AddMassSellProtoId(protoId);

        foreach (var protoId in inventory.StackProtoToType.Keys)
            AddMassSellProtoId(protoId);

        _protoIdsScratch.Sort(CompareMassSellProtoIds);
    }

    private void AddMassSellProtoId(string protoId)
    {
        if (_protoIdsSeenScratch.Add(protoId))
            _protoIdsScratch.Add(protoId);
    }

    private int CompareMassSellProtoIds(string left, string right)
    {
        var depth = GetInheritanceDepth(right).CompareTo(GetInheritanceDepth(left));
        if (depth != 0)
            return depth;

        return OrdinalIds.Compare(left, right);
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

        var depthCmp = GetInheritanceDepth(right.ProductEntity).CompareTo(GetInheritanceDepth(left.ProductEntity));
        if (depthCmp != 0)
            return depthCmp;

        var productCmp = OrdinalIds.Compare(left.ProductEntity, right.ProductEntity);
        if (productCmp != 0)
            return productCmp;

        return OrdinalIds.Compare(left.Id, right.Id);
    }

    private Dictionary<string, List<string>>? BuildMassSellDescendantMatches(
        Dictionary<string, EntityPrototype> protoCache)
    {
        var descendantExpected = CollectMassSellDescendantExpected();
        if (descendantExpected.Count == 0 || _protoIdsScratch.Count == 0)
            return null;

        var matchesByExpected = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        for (var i = 0; i < _protoIdsScratch.Count; i++)
        {
            var protoId = _protoIdsScratch[i];
            if (!TryResolveMassSellPrototype(protoId, protoCache, out var proto))
                continue;

            foreach (var ancestor in _inventory.GetProtoAndAncestors(proto))
            {
                if (!descendantExpected.Contains(ancestor))
                    continue;

                AddMassSellDescendantMatch(matchesByExpected, ancestor, protoId);
            }
        }

        return matchesByExpected;
    }

    private HashSet<string> CollectMassSellDescendantExpected()
    {
        var descendantExpected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var listing in _sellListingsScratch)
        {
            if (_inventory.ResolveMatchMode(listing.ProductEntity, listing.MatchMode) == PrototypeMatchMode.Descendants)
                descendantExpected.Add(listing.ProductEntity);
        }

        return descendantExpected;
    }

    private bool TryResolveMassSellPrototype(
        string protoId,
        Dictionary<string, EntityPrototype> protoCache,
        out EntityPrototype proto)
    {
        if (protoCache.TryGetValue(protoId, out proto!))
            return true;

        if (!_protos.TryIndex<EntityPrototype>(protoId, out proto!))
            return false;

        protoCache[protoId] = proto;
        return true;
    }

    private static void AddMassSellDescendantMatch(
        Dictionary<string, List<string>> matchesByExpected,
        string expectedProtoId,
        string actualProtoId)
    {
        if (!matchesByExpected.TryGetValue(expectedProtoId, out var matches))
        {
            matches = new List<string>();
            matchesByExpected[expectedProtoId] = matches;
        }

        matches.Add(actualProtoId);
    }

    private void ApplyMassSellListings(
        MassSellInventoryState inventory,
        Dictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes,
        Dictionary<string, List<string>>? matchesByExpected,
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
                inventory,
                matchesByExpected);
            if (taken <= 0)
                continue;

            RecordMassSellStep(plan, listing, quote, taken);
        }
    }

    private int ComputeMassSellListingTake(
        NcStoreListingDef listing,
        int unitPrice,
        string stackComponentName,
        MassSellInventoryState inventory,
        Dictionary<string, List<string>>? matchesByExpected)
    {
        if (!TryComputeMassSellWantedUnits(listing.RemainingCount, unitPrice, out var want))
            return 0;

        var expectedStackType = TryGetMassSellExpectedStackType(listing.ProductEntity, stackComponentName);
        if (!string.IsNullOrEmpty(expectedStackType))
            return ReserveMassSellStackUnits(expectedStackType, want, inventory.StackTypeCounts);

        if (_protoIdsScratch.Count == 0)
            return 0;

        var effectiveMatch = _inventory.ResolveMatchMode(listing.ProductEntity, listing.MatchMode);
        return effectiveMatch != PrototypeMatchMode.Descendants
            ? ReserveMassSellProtoUnits(listing.ProductEntity, want, inventory.ProtoCounts)
            : ReserveMassSellDescendantUnits(listing.ProductEntity, want, inventory, matchesByExpected);
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

    private int ReserveMassSellDescendantUnits(
        string expectedProtoId,
        int want,
        MassSellInventoryState inventory,
        Dictionary<string, List<string>>? matchesByExpected)
    {
        if (matchesByExpected == null ||
            !matchesByExpected.TryGetValue(expectedProtoId, out var matchingProtoIds) ||
            matchingProtoIds.Count == 0)
        {
            return 0;
        }

        var taken = 0;

        for (var i = 0; i < matchingProtoIds.Count && taken < want; i++)
        {
            var protoId = matchingProtoIds[i];
            var isStackProto = inventory.StackProtoToType.TryGetValue(protoId, out var stackTypeId) &&
                !string.IsNullOrWhiteSpace(stackTypeId);

            var reserved = isStackProto
                ? ReserveMassSellStackUnits(stackTypeId!, want - taken, inventory.StackTypeCounts)
                : ReserveMassSellProtoUnits(protoId, want - taken, inventory.ProtoCounts);

            taken += reserved;
        }

        return taken;
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
