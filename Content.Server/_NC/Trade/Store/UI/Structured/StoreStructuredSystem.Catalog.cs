using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class StoreStructuredSystem
{
    private void SendCatalog(EntityUid store, NcStoreComponent comp, EntityUid user)
    {
        if (!_ui.IsUiOpen(store, StoreUiKey.Key, user))
            return;

        var catalog = GetOrBuildCatalog(store, comp);
        var msg = BuildCatalogMessage(comp, catalog);
        _ui.ServerSendUiMessage((store, null), StoreUiKey.Key, msg, user);
    }

    private List<StoreListingStaticData> GetOrBuildCatalog(EntityUid store, NcStoreComponent comp)
    {
        if (_catalogCache.TryGet(store, comp.CatalogRevision, out var cached))
            return cached;

        var list = BuildCatalogEntries(comp);
        _catalogCache.Set(store, comp.CatalogRevision, list);
        return list;
    }

    private List<StoreListingStaticData> BuildCatalogEntries(NcStoreComponent comp)
    {
        var list = new List<StoreListingStaticData>(comp.Listings.Count);

        foreach (var listing in comp.Listings)
        {
            if (!TryBuildCatalogEntry(comp, listing, out var entry))
                continue;

            list.Add(entry);
        }

        return list;
    }

    private bool TryBuildCatalogEntry(
        NcStoreComponent comp,
        NcStoreListingDef listing,
        out StoreListingStaticData entry
    )
    {
        entry = null!;

        if (string.IsNullOrWhiteSpace(listing.Id))
            return false;

        if (listing.Mode != StoreMode.Barter && string.IsNullOrWhiteSpace(listing.ProductEntity))
            return false;

        var currencyId = string.Empty;
        var price = 0;
        if (listing.Mode != StoreMode.Barter && !TryPickUiCurrencyAndPrice(comp, listing, out currencyId, out price))
            return false;

        var category = listing.Categories.Count > 0
            ? listing.Categories[0]
            : Loc.GetString("nc-store-category-fallback");

        entry = new(
            listing.Id,
            listing.Mode,
            category,
            listing.ProductEntity,
            listing.MatchMode,
            price,
            currencyId,
            listing.UnitsPerPurchase,
            listing.DisplayName,
            listing.Description,
            CloneBarterCostForCatalog(listing.BarterCost),
            CloneBarterReceiveForCatalog(listing.BarterReceive),
            CloneBarterReceivePoolsForCatalog(listing.BarterReceivePools)
        );

        return true;
    }

    private StoreCatalogMessage BuildCatalogMessage(
        NcStoreComponent comp,
        List<StoreListingStaticData> list
    )
    {
        var (hasBuy, hasSell, hasBarter) = GetCatalogModeFlags(list);
        var uiColors = ResolveUiColors(comp);

        return new(
            comp.CatalogRevision,
            list,
            hasBuy,
            hasSell,
            hasBarter,
            HasContractsProfile(comp),
            uiColors
        );
    }

    private StoreUiColorsData ResolveUiColors(NcStoreComponent comp)
    {
        if (_prototypes.TryIndex(comp.Profile, out var profile) &&
            profile.Theme is { } themeId &&
            _prototypes.TryIndex(themeId, out var theme))
            return CloneUiColors(theme.Colors);

        return new();
    }

    private bool HasContractsProfile(NcStoreComponent comp)
    {
        if (!_prototypes.TryIndex(comp.Profile, out var profile))
            return false;

        if (profile.Contracts is not { } contracts)
            return false;

        return _prototypes.HasIndex(contracts);
    }

    private static StoreUiColorsData CloneUiColors(StoreUiColorsData colors) =>
        new()
        {
            TabsShellBackground = colors.TabsShellBackground,
            TabsShellBorder = colors.TabsShellBorder,
            TabsFrameBackground = colors.TabsFrameBackground,
            TabsFrameBorder = colors.TabsFrameBorder,
            TabContentBackground = colors.TabContentBackground,
            TabsBarBackground = colors.TabsBarBackground,
            TabsBarBorder = colors.TabsBarBorder,
            TabActiveBackground = colors.TabActiveBackground,
            TabActiveBorder = colors.TabActiveBorder,
            TabInactiveBackground = colors.TabInactiveBackground,
            TabInactiveBorder = colors.TabInactiveBorder,
            TabFontActive = colors.TabFontActive,
            TabFontInactive = colors.TabFontInactive,
            CategoriesPanelBackground = colors.CategoriesPanelBackground,
            CategoriesDivider = colors.CategoriesDivider,
            CategoryButtonIdle = colors.CategoryButtonIdle,
            CategoryButtonSelected = colors.CategoryButtonSelected,
            HeaderBackground = colors.HeaderBackground,
            HeaderBorder = colors.HeaderBorder,
            HeaderBalanceText = colors.HeaderBalanceText,
            SearchBoxBackground = colors.SearchBoxBackground,
            SearchBoxBorder = colors.SearchBoxBorder,
            SearchIconColor = colors.SearchIconColor,
            ListingCardBackground = colors.ListingCardBackground,
            ListingCardBorder = colors.ListingCardBorder,
            ListingDivider = colors.ListingDivider,
            ListingTitleColor = colors.ListingTitleColor
        };

    private static (bool HasBuy, bool HasSell, bool HasBarter) GetCatalogModeFlags(List<StoreListingStaticData> list)
    {
        var hasBuy = false;
        var hasSell = false;
        var hasBarter = false;

        foreach (var listing in list)
        {
            if (listing.Mode == StoreMode.Buy)
                hasBuy = true;
            else if (listing.Mode == StoreMode.Sell)
                hasSell = true;
            else if (listing.Mode == StoreMode.Barter)
                hasBarter = true;

            if (hasBuy && hasSell && hasBarter)
                break;
        }

        return (hasBuy, hasSell, hasBarter);
    }

    private static List<NcBarterCostEntry> CloneBarterCostForCatalog(List<NcBarterCostEntry> source)
    {
        var result = new List<NcBarterCostEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var c = source[i];
            result.Add(
                new()
                {
                    Prototype = c.Prototype,
                    Group = c.Group,
                    TagTarget = c.TagTarget,
                    Currency = c.Currency,
                    Count = c.Count
                });
        }

        return result;
    }

    private static List<NcBarterReceiveEntry> CloneBarterReceiveForCatalog(List<NcBarterReceiveEntry> source)
    {
        var result = new List<NcBarterReceiveEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var r = source[i];
            result.Add(
                new()
                {
                    Prototype = r.Prototype,
                    Currency = r.Currency,
                    Count = r.Count
                });
        }

        return result;
    }

    private static List<NcBarterReceivePoolEntry> CloneBarterReceivePoolsForCatalog(
        List<NcBarterReceivePoolEntry> source
    )
    {
        var result = new List<NcBarterReceivePoolEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var r = source[i];
            result.Add(
                new()
                {
                    Pool = r.Pool,
                    Rolls = r.Rolls,
                    Chance = r.Chance
                });
        }

        return result;
    }

    private bool TryPickUiCurrencyAndPrice(
        NcStoreComponent comp,
        NcStoreListingDef listing,
        out string currencyId,
        out int price
    )
    {
        currencyId = string.Empty;
        price = 0;
        if (listing.Cost.Count == 0)
            return false;
        foreach (var cur in comp.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;
            if (listing.Cost.TryGetValue(cur, out var p) && p > 0)
            {
                currencyId = cur;
                price = p;
                return true;
            }
        }

        KeyValuePair<string, int>? best = null;
        foreach (var kv in listing.Cost)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0)
                continue;

            if (best == null || string.CompareOrdinal(kv.Key, best.Value.Key) < 0)
                best = kv;
        }

        if (best == null)
            return false;

        currencyId = best.Value.Key;
        price = best.Value.Value;
        return true;
    }
}
