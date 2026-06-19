using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private MassSellCatalogCache GetMassSellCatalogCache(EntityUid storeUid, NcStoreComponent store)
    {
        if (!_massSellCatalogCache.TryGetValue(storeUid, out var cache))
        {
            cache = new();
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
                cache.ListingQuotes[listing.Id] = (currencyId, unitPrice);
            else
                cache.ListingQuotes[listing.Id] = (string.Empty, 0);
        }

        PrepareMassSellListings(store, cache.ListingQuotes, cache.SellListings);
        cache.CatalogRevision = store.CatalogRevision;
    }

    private void PrepareMassSellListings(
        NcStoreComponent store,
        IReadOnlyDictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes,
        List<NcStoreListingDef> sellListings
    )
    {
        sellListings.Clear();

        foreach (var listing in store.Listings)
        {
            if (listing.Mode != StoreMode.Sell || string.IsNullOrEmpty(listing.ProductEntity) ||
                listing.RemainingCount == 0)
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
        IReadOnlyDictionary<string, (string CurrencyId, int UnitPrice)> listingQuotes
    )
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

    private static int CompareMassSellMatchModePriority(PrototypeMatchMode left, PrototypeMatchMode right) =>
        GetMassSellMatchModePriority(left).CompareTo(GetMassSellMatchModePriority(right));

    private static int GetMassSellMatchModePriority(PrototypeMatchMode mode) =>
        mode switch
        {
            PrototypeMatchMode.Exact => 0,
            PrototypeMatchMode.Matcher => 1,
            PrototypeMatchMode.Tag => 2,
            _ => 3
        };
}
