using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class StoreCatalogMessage : BoundUserInterfaceMessage
{
    public StoreCatalogMessage(
        int catalogRevision,
        List<StoreListingStaticData> listings,
        bool hasBuyTab,
        bool hasSellTab,
        bool hasBarterTab,
        bool hasContractsTab,
        StoreUiColorsData? uiColors = null
    )
    {
        CatalogRevision = catalogRevision;
        Listings = listings;
        HasBuyTab = hasBuyTab;
        HasSellTab = hasSellTab;
        HasBarterTab = hasBarterTab;
        HasContractsTab = hasContractsTab;
        UiColors = uiColors ?? new StoreUiColorsData();
    }

    public int CatalogRevision { get; }
    public List<StoreListingStaticData> Listings { get; }
    public bool HasBuyTab { get; }
    public bool HasSellTab { get; }
    public bool HasBarterTab { get; }
    public bool HasContractsTab { get; }
    public StoreUiColorsData UiColors { get; }
}

[Serializable, NetSerializable,]
public sealed class StoreListingStaticData
{
    public StoreListingStaticData(
        string id,
        StoreMode mode,
        string category,
        string productEntity,
        PrototypeMatchMode matchMode,
        int basePrice,
        string currencyId,
        int unitsPerPurchase,
        string displayName = "",
        string description = "",
        List<NcBarterCostEntry>? barterCost = null,
        List<NcBarterReceiveEntry>? barterReceive = null,
        List<NcBarterReceivePoolEntry>? barterReceivePools = null
    )
    {
        Id = id;
        Mode = mode;
        Category = category;
        ProductEntity = productEntity;
        MatchMode = matchMode;
        BasePrice = basePrice;
        CurrencyId = currencyId;
        UnitsPerPurchase = unitsPerPurchase;
        DisplayName = displayName;
        Description = description;
        BarterCost = barterCost ?? new List<NcBarterCostEntry>();
        BarterReceive = barterReceive ?? new List<NcBarterReceiveEntry>();
        BarterReceivePools = barterReceivePools ?? new List<NcBarterReceivePoolEntry>();
    }

    public string Id { get; }
    public StoreMode Mode { get; }
    public string Category { get; }
    public string ProductEntity { get; }
    public PrototypeMatchMode MatchMode { get; }
    public int BasePrice { get; }
    public string CurrencyId { get; }
    public int UnitsPerPurchase { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public List<NcBarterCostEntry> BarterCost { get; }
    public List<NcBarterReceiveEntry> BarterReceive { get; }
    public List<NcBarterReceivePoolEntry> BarterReceivePools { get; }
}

[DataDefinition, Serializable, NetSerializable,]
public sealed partial class StoreUiColorsData
{
    [DataField("tabsShellBackground")] public string TabsShellBackground { get; set; } = "#17181D";
    [DataField("tabsShellBorder")] public string TabsShellBorder { get; set; } = "#7A6334";

    [DataField("tabsFrameBackground")] public string TabsFrameBackground { get; set; } = "#1E2027";
    [DataField("tabsFrameBorder")] public string TabsFrameBorder { get; set; } = "#4C4438";
    [DataField("tabContentBackground")] public string TabContentBackground { get; set; } = "#1E2027";

    [DataField("tabsBarBackground")] public string TabsBarBackground { get; set; } = "#1C1E25";
    [DataField("tabsBarBorder")] public string TabsBarBorder { get; set; } = "#665C4E";

    [DataField("tabActiveBackground")] public string TabActiveBackground { get; set; } = "#6B5730";
    [DataField("tabActiveBorder")] public string TabActiveBorder { get; set; } = "#D4B06A";

    [DataField("tabInactiveBackground")] public string TabInactiveBackground { get; set; } = "#2C2E35";
    [DataField("tabInactiveBorder")] public string TabInactiveBorder { get; set; } = "#665C4E";

    [DataField("tabFontActive")] public string TabFontActive { get; set; } = "#F0D49A";
    [DataField("tabFontInactive")] public string TabFontInactive { get; set; } = "#B9AE95";

    [DataField("categoriesPanelBackground")]
    public string CategoriesPanelBackground { get; set; } = "#1A1B20";

    [DataField("categoriesDivider")] public string CategoriesDivider { get; set; } = "#7A6334";
    [DataField("categoryButtonIdle")] public string CategoryButtonIdle { get; set; } = "#7C6624";
    [DataField("categoryButtonSelected")] public string CategoryButtonSelected { get; set; } = "#D9A441";

    [DataField("headerBackground")] public string HeaderBackground { get; set; } = "#202329";
    [DataField("headerBorder")] public string HeaderBorder { get; set; } = "#4C4438";
    [DataField("headerBalanceText")] public string HeaderBalanceText { get; set; } = "#FFFF00";
    [DataField("searchBoxBackground")] public string SearchBoxBackground { get; set; } = "#2B2E35";
    [DataField("searchBoxBorder")] public string SearchBoxBorder { get; set; } = "#4C4438";
    [DataField("searchIconColor")] public string SearchIconColor { get; set; } = "#FFFFFF";

    [DataField("listingCardBackground")] public string ListingCardBackground { get; set; } = "#141417E6";
    [DataField("listingCardBorder")] public string ListingCardBorder { get; set; } = "#B08D3B";
    [DataField("listingDivider")] public string ListingDivider { get; set; } = "#B08D3BCC";
    [DataField("listingTitleColor")] public string ListingTitleColor { get; set; } = "#BFA462";
}
