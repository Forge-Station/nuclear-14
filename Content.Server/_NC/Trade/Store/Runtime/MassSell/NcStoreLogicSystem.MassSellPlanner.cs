using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
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

    public void ClearStoreRuntimeCaches(EntityUid store) => _massSellCatalogCache.Remove(store);

    private MassSellPlan ComputeMassSellPlanInternal(
        EntityUid storeUid,
        NcStoreComponent store,
        IReadOnlyList<EntityUid> items
    )
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

    private static MassSellPlan CreateEmptyMassSellPlan() =>
        new(
            new(StringComparer.Ordinal),
            new(StringComparer.Ordinal),
            new Dictionary<string, (string, int)>(StringComparer.Ordinal),
            new());

    private sealed class MassSellCatalogCache
    {
        public readonly Dictionary<string, (string CurrencyId, int UnitPrice)> ListingQuotes =
            new(StringComparer.Ordinal);

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
}
