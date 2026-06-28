using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[DataDefinition, Serializable, NetSerializable,]
public sealed partial class NcBarterCostEntry
{
    /// <summary>Exact entity prototype the player must give.</summary>
    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    /// <summary>ncItemGroup id. Groups are valid only for checking existing player items.</summary>
    [DataField("group")]
    public string Group { get; set; } = string.Empty;

    /// <summary>ncTradeTag id. Trade tag targets wrap a TagPrototype plus UI metadata.</summary>
    [DataField("tagTarget")]
    public string TagTarget { get; set; } = string.Empty;

    /// <summary>Stack currency id the player must pay.</summary>
    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    [DataField("count")]
    public int Count { get; set; } = 1;
}

[DataDefinition, Serializable, NetSerializable,]
public sealed partial class NcBarterReceiveEntry
{
    /// <summary>Exact entity prototype to give to the player.</summary>
    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    /// <summary>Stack currency id to give to the player.</summary>
    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    [DataField("count")]
    public int Count { get; set; } = 1;
}

[DataDefinition, Serializable, NetSerializable,]
public sealed partial class NcBarterReceivePoolEntry
{
    /// <summary>Weighted reward pool id. Uses ncSupplyRewardPool entries.</summary>
    [DataField("pool", required: true)]
    public string Pool { get; set; } = string.Empty;

    /// <summary>How many times the pool is rolled per one barter execution.</summary>
    [DataField("rolls")]
    public IntRange Rolls { get; set; } = IntRange.Fixed(1);

    /// <summary>Chance to roll this pool per one barter execution.</summary>
    [DataField("chance")]
    public float Chance { get; set; } = 1.0f;
}

[Prototype("ncBarterListing")]
public sealed partial class NcBarterListingPrototype : IPrototype
{
    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    /// <summary>Optional entity prototype id used as card icon. If empty, the first receive/cost item is used.</summary>
    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    /// <summary>How many times this barter can be performed. -1 means unlimited.</summary>
    [DataField("count")]
    public int Count { get; private set; } = -1;

    [DataField("cost", required: true)]
    public List<NcBarterCostEntry> Cost { get; private set; } = new();

    [DataField("receive", required: false)]
    public List<NcBarterReceiveEntry> Receive { get; private set; } = new();

    /// <summary>Optional random receive pools. Cost remains fixed; only receive side can be random.</summary>
    [DataField("receivePools", required: false)]
    public List<NcBarterReceivePoolEntry> ReceivePools { get; private set; } = new();

    [IdDataField]
    public string ID { get; private set; } = default!;
}

[Prototype("ncBarterCategory")]
public sealed partial class NcBarterCategoryPrototype : IPrototype
{
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>References to standalone ncBarterListing prototypes.</summary>
    [DataField("listings", required: true)]
    public List<ProtoId<NcBarterListingPrototype>> Listings { get; private set; } = new();

    [IdDataField]
    public string ID { get; private set; } = default!;
}

[Prototype("ncBarterPreset")]
public sealed partial class NcBarterPresetPrototype : IPrototype
{
    [DataField("categories", required: true)]
    public List<ProtoId<NcBarterCategoryPrototype>> Categories { get; private set; } = new();

    [IdDataField]
    public string ID { get; private set; } = default!;
}
