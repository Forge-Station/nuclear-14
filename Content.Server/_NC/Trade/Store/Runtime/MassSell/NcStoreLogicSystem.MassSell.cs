using System.Linq;
using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public MassSellPlan ComputeMassSellPlan(NcStoreComponent store, EntityUid container)
    {
        InvalidateInventoryCache(container);
        var cached = GetOrBuildDeepItemsCache(container);
        CompactCachedItems(cached);
        return ComputeMassSellPlanInternal(store, cached);
    }

    public MassSellPlan ComputeMassSellPlanFromCachedItems(
        NcStoreComponent store,
        EntityUid container,
        IReadOnlyList<EntityUid> cachedItems
    ) =>
        ComputeMassSellPlanInternal(store, cachedItems);

    private MassSellPlan ComputeMassSellPlanInternal(
        NcStoreComponent store,
        IEnumerable<EntityUid> items
    )
    {
        var incomeByCurrency = new Dictionary<string, int>();
        var unitsByListingId = new Dictionary<string, int>();
        var priceByListingId = new Dictionary<string, (string, int)>();
        var steps = new List<MassSellStep>();

        if (store.Listings.Count == 0)
            return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);

        var stackTypeCounts = new Dictionary<string, int>();
        var protoCounts = new Dictionary<string, int>();
        var protoCache = new Dictionary<string, EntityPrototype>();
        var stackProtoToType = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var ent in items)
        {
            if (!_ents.EntityExists(ent))
                continue;
            if (_ents.TryGetComponent(ent, out StackComponent? st))
            {
                var cnt = Math.Max(st.Count, 0);
                if (cnt > 0 && !string.IsNullOrWhiteSpace(st.StackTypeId))
                {
                    stackTypeCounts.TryGetValue(st.StackTypeId, out var prev);
                    stackTypeCounts[st.StackTypeId] = prev + cnt;
                }
                if (_ents.TryGetComponent(ent, out MetaDataComponent? stMeta) && stMeta.EntityPrototype is { } stProto)
                {
                    protoCache[stProto.ID] = stProto;
                    if (!string.IsNullOrWhiteSpace(st.StackTypeId))
                        stackProtoToType.TryAdd(stProto.ID, st.StackTypeId);
                }

                continue;
            }

            if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is null)
                continue;

            var proto = meta.EntityPrototype;

            if (!protoCounts.TryAdd(proto.ID, 1))
                protoCounts[proto.ID] += 1;

            protoCache[proto.ID] = proto;
        }

        if (stackTypeCounts.Count == 0 && protoCounts.Count == 0)
            return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);
        var protoIds = protoCounts.Count > 0 || stackProtoToType.Count > 0
            ? protoCounts.Keys
                .Concat(stackProtoToType.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(GetInheritanceDepth)
                .ThenBy(x => x, OrdinalIds)
                .ToArray()
            : Array.Empty<string>();

        var listingPrices = new Dictionary<string, int>();
        foreach (var l in store.Listings)
        {
            if (l.Mode != StoreMode.Sell)
                continue;
            if (TryPickCurrencyForSell(store, l, out _, out var price))
                listingPrices[l.Id] = price;
            else
                listingPrices[l.Id] = 0;
        }

        var sellListings = store.Listings
            .Where(l =>
                l.Mode == StoreMode.Sell &&
                !string.IsNullOrEmpty(l.ProductEntity) &&
                l.RemainingCount != 0 &&
                listingPrices.TryGetValue(l.Id, out var p) && p > 0)
            .OrderByDescending(l => listingPrices[l.Id]) // Сначала дорогие
            .ThenByDescending(l => GetInheritanceDepth(l.ProductEntity))
            .ThenBy(l => l.ProductEntity, OrdinalIds)
            .ThenBy(l => l.Id, OrdinalIds)
            .ToArray();

        if (sellListings.Length == 0)
            return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);

        var stackName = _compFactory.GetComponentName(typeof(StackComponent));

        foreach (var listing in sellListings)
        {
            if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var unitPrice))
                continue;

            if (unitPrice <= 0 || string.IsNullOrWhiteSpace(currencyId))
                continue;

            var remaining = listing.RemainingCount;
            if (remaining < -1)
                remaining = -1;

            var maxByRemaining = remaining >= 0 ? remaining : int.MaxValue;
            if (maxByRemaining <= 0)
                continue;

            var maxTakeByInt = int.MaxValue / unitPrice;
            if (maxTakeByInt <= 0)
                continue;

            var want = Math.Min(maxByRemaining, maxTakeByInt);

            string? expectedStackType = null;
            if (_protos.TryIndex<EntityPrototype>(listing.ProductEntity, out var prodProto) &&
                prodProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                expectedStackType = prodStackDef.StackTypeId;

            var taken = 0;
            var effectiveMatch = ResolveMatchMode(listing.ProductEntity, listing.MatchMode);

            if (!string.IsNullOrEmpty(expectedStackType))
            {
                if (!stackTypeCounts.TryGetValue(expectedStackType, out var available) || available <= 0)
                    continue;

                taken = Math.Min(available, want);
                stackTypeCounts[expectedStackType] = available - taken;
            }
            else
            {
                if (protoIds.Length == 0)
                    continue;

                if (effectiveMatch != PrototypeMatchMode.Descendants)
                {
                    if (!protoCounts.TryGetValue(listing.ProductEntity, out var available) || available <= 0)
                        continue;

                    taken = Math.Min(available, want);
                    protoCounts[listing.ProductEntity] = available - taken;
                }
                else
                {
                    foreach (var protoId in protoIds)
                    {
                        if (taken >= want)
                            break;

                        var isStackProto = stackProtoToType.TryGetValue(protoId, out var stType) &&
                            !string.IsNullOrWhiteSpace(stType);

                        int available;
                        if (isStackProto)
                        {
                            if (!stackTypeCounts.TryGetValue(stType!, out available) || available <= 0)
                                continue;
                        }
                        else
                        {
                            if (!protoCounts.TryGetValue(protoId, out available) || available <= 0)
                                continue;
                        }

                        if (!protoCache.TryGetValue(protoId, out var proto) &&
                            !_protos.TryIndex(protoId, out proto))
                            continue;

                        protoCache[protoId] = proto;

                        if (!IsProtoOrDescendant(proto, listing.ProductEntity))
                            continue;

                        var take = Math.Min(available, want - taken);
                        if (take <= 0)
                            continue;

                        if (isStackProto)
                            stackTypeCounts[stType!] = available - take;
                        else
                            protoCounts[protoId] = available - take;
                        taken += take;
                    }
                }
            }

            if (taken <= 0)
                continue;

            var total = (long) unitPrice * taken;
            SafeAddIncome(incomeByCurrency, currencyId, total);

            unitsByListingId[listing.Id] = taken;
            priceByListingId[listing.Id] = (currencyId, unitPrice);
            steps.Add(new(listing, currencyId, unitPrice, taken));
        }

        return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);
    }

    public bool TryMassSellFromContainer(
        EntityUid machine,
        NcStoreComponent store,
        EntityUid user,
        EntityUid container
    )
    {
        if (store.Listings.Count == 0)
            return false;

        InvalidateInventoryCache(container);

        _scratchItems.Clear();
        var cached = GetOrBuildDeepItemsCache(container);
        CompactCachedItems(cached);

        foreach (var item in cached)
            _scratchItems.Add(item);

        var cachedItems = _scratchItems;

        var plan = ComputeMassSellPlanFromCachedItems(store, container, cachedItems);
        if (plan.Steps.Count == 0 || plan.IncomeByCurrency.Count == 0)
            return false;

        var incomeActual = new Dictionary<string, int>();
        var any = false;

        foreach (var step in plan.Steps)
        {
            if (step.Count <= 0 ||
                step.UnitPrice <= 0 ||
                string.IsNullOrWhiteSpace(step.CurrencyId) ||
                string.IsNullOrWhiteSpace(step.Listing.ProductEntity))
                continue;

            var listing = step.Listing;

            var remaining = listing.RemainingCount;
            if (remaining < -1)
                remaining = -1;

            var maxByRemaining = remaining >= 0 ? remaining : int.MaxValue;
            if (maxByRemaining <= 0)
                continue;

            var take = Math.Min(step.Count, maxByRemaining);
            if (take <= 0)
                continue;

            if (!TryTakeProductUnitsFromCachedList(
                container,
                cachedItems,
                listing.ProductEntity,
                take,
                listing.MatchMode))
                continue;

            if (listing.RemainingCount > 0)
                listing.RemainingCount = Math.Max(0, listing.RemainingCount - take);

            var total = (long) step.UnitPrice * take;
            SafeAddIncome(incomeActual, step.CurrencyId, total);

            any = true;
        }

        if (!any || incomeActual.Count == 0)
            return false;

        foreach (var (currency, amount) in incomeActual)
        {
            if (amount <= 0)
                continue;

            GiveCurrency(user, currency, amount);
        }

        InvalidateInventoryCache(container);
        InvalidateInventoryCache(user);

        return true;
    }





    public Dictionary<string, int> GetMassSellValue(
        NcStoreComponent store,
        EntityUid container
    ) =>
        ComputeMassSellPlan(store, container).IncomeByCurrency;

    public readonly record struct MassSellStep(
        StoreListingPrototype Listing,
        string CurrencyId,
        int UnitPrice,
        int Count);

    public readonly record struct MassSellPlan(
        Dictionary<string, int> IncomeByCurrency,
        Dictionary<string, int> UnitsByListingId,
        Dictionary<string, (string CurrencyId, int UnitPrice)> PriceByListingId,
        List<MassSellStep> Steps);
}
