using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[DataDefinition]
public sealed partial class StoreCatalogEntry
{
    [DataField("match")] public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;
    [DataField("price", required: true)] public int Price;
    [DataField("proto", required: true)] public string Proto = string.Empty;
    [DataField("count")] public int? Count { get; set; }
    [DataField("amount")] public int Amount { get; set; } = 1;
}

[Prototype("storeCategoryStructured")]
public sealed partial class StoreCategoryStructuredPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("entries", required: true)]
    public List<StoreCatalogEntry> Entries { get; private set; } = new();
}

[Prototype("storePresetStructured")]
public sealed partial class StorePresetStructuredPrototype : IPrototype
{
    [DataField("categories", required: true)]
    public List<string> Categories { get; private set; } = new();

    [DataField("currency", required: true)]
    public string Currency = string.Empty;

    [IdDataField]
    public string ID { get; private set; } = default!;
}

[Prototype("ncStoreUiTheme")]
public sealed partial class StoreUiThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("colors", required: true)]
    public StoreUiColorsData Colors { get; private set; } = new();
}

[Prototype("ncStoreProfile")]
public sealed partial class NcStoreProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("buy")]
    public List<ProtoId<StorePresetStructuredPrototype>> Buy { get; private set; } = new();

    [DataField("sell")]
    public List<ProtoId<StorePresetStructuredPrototype>> Sell { get; private set; } = new();

    /// <summary>
    /// Barter presets. Actual exchanges are standalone ncBarterListing prototypes, grouped by ncBarterCategory listings.
    /// Execution is handled only by the Barter transaction path.
    /// </summary>
    [DataField("barter")]
    public List<ProtoId<NcBarterPresetPrototype>> Barter { get; private set; } = new();

    [DataField("contracts")]
    public ProtoId<StoreContractsPresetPrototype>? Contracts { get; private set; }

    [DataField("theme")]
    public ProtoId<StoreUiThemePrototype>? Theme { get; private set; }
}


[DataDefinition, Serializable, NetSerializable]
public sealed partial class ContractPointSelectorPrototype
{
    [DataField("type")]
    public ContractPointSelectorType Type { get; set; } = ContractPointSelectorType.Store;

    [DataField("id")]
    public string Id { get; set; } = string.Empty;

    [DataField("options")]
    public List<WeightedContractPointOptionEntry> Options { get; set; } = new();
}

[DataDefinition, Serializable, NetSerializable]
public partial struct WeightedContractPointOptionEntry
{
    [DataField("type")]
    public ContractPointSelectorType Type;

    [DataField("id", required: true)]
    public string Id;

    [DataField("weight")]
    public int Weight;

    public WeightedContractPointOptionEntry(ContractPointSelectorType type, string id, int weight)
    {
        Type = type;
        Id = id;
        Weight = weight;
    }
}

[Serializable, NetSerializable]
public enum ContractPointSelectorType : byte
{
    Store = 0,
    MarkerId = 1,
    MarkerGroup = 2,
    Weighted = 3
}

[Prototype("storeContractsPreset")]
public sealed partial class StoreContractsPresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("contractOffers", required: true)]
    public NcContractOffersPrototype? ContractOffers { get; set; }

    [DataField("skipCost")]
    public int SkipCost { get; set; } = 360;

    [DataField("skipCurrency")]
    public string SkipCurrency { get; set; } = string.Empty;
}

[Prototype("ncContractOfferPool")]
public sealed partial class NcContractOfferPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("order")]
    public int Order { get; private set; }

    [DataField("color")]
    public string Color { get; private set; } = string.Empty;

    [DataField("entries", required: true)]
    public List<NcContractOfferEntry> Entries { get; private set; } = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class NcContractOffersPrototype
{
    [DataField("maxVisible")]
    public int MaxVisible { get; set; } = 8;

    [DataField("groups", required: true)]
    public List<NcContractOfferGroupEntry> Groups { get; set; } = new();
}

[DataDefinition, Serializable, NetSerializable]
public partial struct NcContractOfferGroupEntry
{
    [DataField("pool", required: true)]
    public ProtoId<NcContractOfferPoolPrototype> Pool;

    [DataField("minVisible")]
    public int MinVisible;

    [DataField("maxVisible")]
    public int MaxVisible = 1;

    [DataField("fillWeight")]
    public int FillWeight = 1;

    public NcContractOfferGroupEntry()
    {
    }
}

[DataDefinition, Serializable, NetSerializable]
public partial struct NcContractOfferEntry
{
    [DataField("type", required: true)]
    public NcContractOfferType Type;

    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("weight")]
    public int Weight = 1;

    public NcContractOfferEntry()
    {
    }
}

[Serializable, NetSerializable]
public enum NcContractOfferType : byte
{
    Supply = 0,
    Retrieval = 1,
    Hunt = 2,
    GhostRole = 3,
}

/// <summary>
/// Trade contracts item group. Groups are only valid for checking already existing turn-in items.
/// They must not be used for spawning or reward generation.
/// </summary>
[Prototype("ncItemGroup")]
public sealed partial class NcItemGroupPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    /// <summary>Optional entity prototype id used only as a UI icon fallback.</summary>
    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("prototypes")]
    public List<string> Prototypes { get; private set; } = new();

    [DataField("tags")]
    public List<string> Tags { get; private set; } = new();
}


[Serializable, NetSerializable]
public enum ContractObjectiveType : byte
{
    Delivery = 0,
    Hunt = 1,
    GhostRole = 3
}
[Serializable, NetSerializable]
public enum PrototypeMatchMode : byte
{
    Exact = 0,

    // Phase M: treat the "proto" field as the ID of an NcMatcherPrototype, not an EntityPrototype.
    // The matcher resolves to a group of prototypes (Items list) and/or tags for flexible match.
    // See NcMatcherPrototype for semantics and loader/matching rules.
    Matcher = 1
}

[Serializable]
public sealed class ListingConditionPrototype
{
    [DataField("condition")]
    public object? Condition;
}
