using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


/// <summary>
///     Flavor of a listing entry on the client (base listing vs derived virtual entries).
/// </summary>
[Serializable, NetSerializable,]
public enum StoreListingFlavor : byte
{
    Base = 0,
    Ready = 1,
    Crate = 2
}

/// <summary>
///     Client-side listing view model (static + dynamic fields).
///     Constructed on the client from <see cref="StoreListingStaticData" /> and <see cref="StoreDynamicState" />.
/// </summary>
[Serializable, NetSerializable,]
public sealed class StoreListingData
{
    public List<NcBarterCostEntry> BarterCost = new();
    public List<NcBarterReceiveEntry> BarterReceive = new();
    public List<NcBarterReceivePoolEntry> BarterReceivePools = new();
    public string Category = string.Empty;
    public string CurrencyId = string.Empty;
    public string Description = string.Empty;
    public string DisplayName = string.Empty;

    /// <summary>Client-only flavor to distinguish derived entries.</summary>
    public StoreListingFlavor Flavor = StoreListingFlavor.Base;

    public string Id = string.Empty;

    /// <summary>Base listing id used for server transactions.</summary>
    public string ListingId = string.Empty;

    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;
    public StoreMode Mode;

    // Dynamic
    /// <summary>
    ///     Per-listing action capacity: player-owned item count for Sell, max affordable execution count for Barter.
    /// </summary>
    public int Owned;

    public int Price;
    public string ProductEntity = string.Empty;
    public int Remaining = -1;
    public int UnitsPerPurchase = 1;

    public StoreListingData() { }

    public StoreListingData(
        string listingId,
        StoreListingFlavor flavor,
        string productEntity,
        PrototypeMatchMode matchMode,
        int price,
        string category,
        string currencyId,
        StoreMode mode,
        int owned = 0,
        int remaining = -1,
        int unitsPerPurchase = 1,
        string displayName = "",
        string description = "",
        List<NcBarterCostEntry>? barterCost = null,
        List<NcBarterReceiveEntry>? barterReceive = null,
        List<NcBarterReceivePoolEntry>? barterReceivePools = null
    )
    {
        ListingId = listingId;
        Flavor = flavor;
        Id = flavor == StoreListingFlavor.Base ? listingId : MakeUiId(listingId, flavor);
        ProductEntity = productEntity;
        MatchMode = matchMode;
        Price = price;
        Category = category;
        CurrencyId = currencyId;
        Mode = mode;
        Owned = owned;
        Remaining = remaining;
        UnitsPerPurchase = Math.Max(1, unitsPerPurchase);
        DisplayName = displayName;
        Description = description;
        BarterCost = barterCost ?? new List<NcBarterCostEntry>();
        BarterReceive = barterReceive ?? new List<NcBarterReceiveEntry>();
        BarterReceivePools = barterReceivePools ?? new List<NcBarterReceivePoolEntry>();
    }

    public StoreListingData(
        string id,
        string productEntity,
        PrototypeMatchMode matchMode,
        int price,
        string category,
        string currencyId,
        StoreMode mode,
        int owned = 0,
        int remaining = -1,
        int unitsPerPurchase = 1,
        string displayName = "",
        string description = "",
        List<NcBarterCostEntry>? barterCost = null,
        List<NcBarterReceiveEntry>? barterReceive = null,
        List<NcBarterReceivePoolEntry>? barterReceivePools = null
    )
    {
        Id = id;
        ListingId = id;
        Flavor = StoreListingFlavor.Base;
        ProductEntity = productEntity;
        MatchMode = matchMode;
        Price = price;
        Category = category;
        CurrencyId = currencyId;
        Mode = mode;
        Owned = owned;
        Remaining = remaining;
        UnitsPerPurchase = Math.Max(1, unitsPerPurchase);
        DisplayName = displayName;
        Description = description;
        BarterCost = barterCost ?? new List<NcBarterCostEntry>();
        BarterReceive = barterReceive ?? new List<NcBarterReceiveEntry>();
        BarterReceivePools = barterReceivePools ?? new List<NcBarterReceivePoolEntry>();
    }

    public static string MakeUiId(string listingId, StoreListingFlavor flavor) =>
        // Unit Separator (0x1F) is extremely unlikely to appear in prototype ids; we use it to avoid collisions.
        listingId + "\u001f" + (byte) flavor;
}
