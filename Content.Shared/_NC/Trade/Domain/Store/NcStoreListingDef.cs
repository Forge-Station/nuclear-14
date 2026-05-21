namespace Content.Shared._NC.Trade;


public sealed class NcStoreListingDef
{
    public string Description = string.Empty;

    public string DisplayName = string.Empty;
    public string Id = string.Empty;

    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;
    public StoreMode Mode = StoreMode.Buy;

    public string ProductEntity = string.Empty;

    public List<NcBarterCostEntry> BarterCost { get; set; } = new();
    public List<NcBarterReceiveEntry> BarterReceive { get; set; } = new();
    public List<NcBarterReceivePoolEntry> BarterReceivePools { get; set; } = new();

    public Dictionary<string, int> Cost { get; set; } = new();

    public List<string> Categories { get; set; } = new();

    public List<ListingConditionPrototype> Conditions { get; set; } = new();

    public int UnitsPerPurchase { get; set; } = 1;

    public int RemainingCount { get; set; } = -1;
}
