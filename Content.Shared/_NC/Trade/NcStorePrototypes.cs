using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
[Prototype("ncStoreListing")]
public sealed class StoreListingPrototype : IPrototype
{
    [IdDataField]
    public string Id = string.Empty;

    [DataField("mode")]
    public StoreMode Mode = StoreMode.Buy;

    [DataField("productEntity")]
    public string ProductEntity = string.Empty;

    [DataField("cost")]
    public Dictionary<string, int> Cost { get; set; } = new();

    [DataField("categories")]
    public List<string> Categories { get; set; } = new();

    [DataField("conditions")]
    public List<ListingConditionPrototype> Conditions { get; set; } = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public int RemainingCount { get; set; } = -1;

    public string ID => Id;
}

[Prototype("storePresetStructured")]
public sealed partial class StorePresetStructuredPrototype : IPrototype
{
    [DataField("catalog", required: true)]
    public Dictionary<string, List<StoreCatalogEntry>> Catalog = new();

    [DataField("currency", required: true)]
    public string Currency = string.Empty;

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataDefinition]
    public sealed partial class StoreCatalogEntry
    {
        [DataField("price", required: true)]
        public int Price;

        [DataField("proto", required: true)]
        public string Proto = string.Empty;

        [DataField("count")]
        public int? Count { get; set; }
    }
}

[Prototype("storeContract")]
public sealed class StoreContractPrototype : IPrototype
{
    [DataField("description")]
    public string Description = string.Empty;

    [DataField("difficulty")]
    public string Difficulty = "Easy";

    [DataField("name", required: true)]
    public string Name = string.Empty;

    [DataField("required")]
    public int Required = 1;

    [DataField("reward")]
    public int Reward;

    [DataField("rewardCurrency")]
    public string RewardCurrency = string.Empty;

    [DataField("rewardItem")]
    public string? RewardItem;

    [DataField("rewardItemCount")]
    public int RewardItemCount;

    [DataField("targetItem", required: true)]
    public string TargetItem = string.Empty;

    [IdDataField]
    public string ID { get; private set; } = default!;
}

[Prototype("storeContractsPreset")]
public sealed class StoreContractsPresetPrototype : IPrototype
{
    [DataField("contracts", required: true)]
    public List<string> Contracts { get; set; } = new();

    [IdDataField]
    public string ID { get; private set; } = default!;
}

[Serializable]
public sealed class ListingConditionPrototype
{
    [DataField("condition")]
    public object? Condition;
}
