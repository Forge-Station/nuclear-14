using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable, Prototype("ncStoreListing")]
public sealed partial class StoreListingPrototype : IPrototype
{
    [IdDataField] public string Id = string.Empty;

    [DataField("match")] public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;
    [DataField("mode")] public StoreMode Mode = StoreMode.Buy;

    [DataField("productEntity")] public string ProductEntity = string.Empty;

    [DataField("cost")] public Dictionary<string, int> Cost { get; set; } = new();

    [DataField("categories")] public List<string> Categories { get; set; } = new();

    [DataField("conditions")] public List<ListingConditionPrototype> Conditions { get; set; } = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public int RemainingCount { get; set; } = -1;

    public string ID => Id;
}

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

    [DataField("contracts")]
    public ProtoId<StoreContractsPresetPrototype>? Contracts { get; private set; }

    [DataField("theme")]
    public ProtoId<StoreUiThemePrototype>? Theme { get; private set; }
}


[Prototype("storeContract")]
public sealed partial class StoreContractPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("match")] public PrototypeMatchMode MatchMode { get; private set; } = PrototypeMatchMode.Exact;

    [DataField("name")] public string Name { get; private set; } = string.Empty;
    [DataField("description")] public string Description { get; private set; } = string.Empty;

    [DataField("difficulty")] public string Difficulty { get; private set; } = "Easy";
    [DataField("repeatable")] public bool Repeatable { get; private set; } = true;
    [DataField("objectiveType")] public ContractObjectiveType ObjectiveType { get; private set; } = ContractObjectiveType.Delivery;
    [DataField("runtime")] public StoreContractRuntimePrototype Runtime { get; private set; } = new();

    [DataField("targetItem")] public string? TargetItem { get; private set; }

    [DataField("required")] public IntRange Required { get; private set; } = IntRange.Fixed(0);

    [DataField("targets")] public List<StoreContractTargetEntry>? Targets { get; private set; }

    [DataField("targetCount")] public IntRange TargetCount { get; private set; } = IntRange.Fixed(1);

    [DataField("rewards")]
    public List<ContractRewardDef> Rewards { get; private set; } = new();
}

[DataDefinition]
public sealed partial class StoreContractTargetEntry
{
    [DataField("id", required: true)] public string TargetItemId { get; set; } = default!;
    [DataField("required")] public IntRange Required { get; set; } = IntRange.Fixed(0);
    [DataField("weight")] public int Weight { get; set; } = 1;
}


[DataDefinition]
public sealed partial class StoreContractRuntimePrototype
{
    [DataField("stageGoal")]
    public int StageGoal { get; set; } = 1;

    [DataField("spawnPoint")]
    public ContractPointSelectorPrototype? SpawnPoint { get; set; }

    [DataField("dropoffPoint")]
    public ContractPointSelectorPrototype? DropoffPoint { get; set; }

    [DataField("targetPrototype")]
    public string TargetPrototype { get; set; } = string.Empty;

    [DataField("deliverySpawnPrototype")]
    public string DeliverySpawnPrototype { get; set; } = string.Empty;

    [DataField("structurePrototype")]
    public string StructurePrototype { get; set; } = string.Empty;

    [DataField("ghostRole")]
    public string GhostRole { get; set; } = string.Empty;

    [DataField("proofPrototype")]
    public string ProofPrototype { get; set; } = string.Empty;

    [DataField("preserveTargetOnComplete")]
    public bool PreserveTargetOnComplete { get; set; }

    [DataField("allowStoreWorldTurnIn")]
    public bool AllowStoreWorldTurnIn { get; set; }

    [DataField("acceptTimeoutSeconds")]
    public int AcceptTimeoutSeconds { get; set; } = 300;

    [DataField("givePinpointer")]
    public bool GivePinpointer { get; set; } = true;

    [DataField("pinpointerPrototype")]
    public string PinpointerPrototype { get; set; } = "PinpointerUniversal";

    [DataField("guardPrototype")]
    public string GuardPrototype { get; set; } = string.Empty;

    [DataField("guardCount")]
    public int GuardCount { get; set; } = 0;

    [DataField("repairToolQuality")]
    public string RepairToolQuality { get; set; } = "Welding";

    [DataField("repairDoAfterSeconds")]
    public float RepairDoAfterSeconds { get; set; } = 2f;

    [DataField("repairStageSound")]
    public string RepairStageSound { get; set; } = "/Audio/Effects/sparks4.ogg";

    // Phase M: if true and the contract's target is a matcher, the system spawns the required
    // number of items for the player (random picks from matcher.Items, may repeat). The player
    // then delivers them to complete the contract. Without this flag the player must find
    // matching items in the world by themselves.
    //
    // Only meaningful for Delivery contracts. Ignored for Hunt (which always spawns), Repair,
    // GhostRole, etc.
    //
    // If set true on a matcher with empty Items — loader emits a warning and treats as false.
    [DataField("spawnItems")]
    public bool SpawnItems { get; set; }

    // Phase M: explicit override list of prototypes to spawn. If non-empty, takes precedence
    // over random picking from matcher.Items — these exact prototypes are spawned (one per
    // required slot up to required count). Useful when the author wants a curated mix like
    // "one pistol + one rifle + one shotgun" instead of random picks that could repeat.
    //
    // Applies to Hunt (spawns exactly these mobs) and to Delivery with SpawnItems=true (spawns
    // exactly these items for the player). If the list has fewer entries than required, the
    // remainder is filled by random picks from matcher.Items.
    [DataField("spawnSpecific")]
    public List<string> SpawnSpecific { get; set; } = new();
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

    [DataField("limits", required: true)]
    public Dictionary<string, int> Limits { get; set; } = new();

    [DataField("packs")]
    public List<PackIncludeEntry> Packs { get; set; } = new();

    [DataField("skipCost")]
    public int SkipCost { get; set; } = 360;

    [DataField("skipCurrency")]
    public string SkipCurrency { get; set; } = string.Empty;
}

[DataDefinition]
public partial struct ContractWeightEntry
{
    [DataField("id", required: true)] public string Id = string.Empty;
    [DataField("weight")] public int Weight = 1;

    public ContractWeightEntry(string id, int weight)
    {
        Id = id;
        Weight = weight;
    }
}

[DataDefinition]
public partial struct PackIncludeEntry
{
    [DataField("id", required: true)] public string Id = string.Empty;
    [DataField("weight")] public int Weight = 1;

    public PackIncludeEntry(string id, int weight)
    {
        Id = id;
        Weight = weight;
    }
}

[Prototype("storeContractPack")]
public sealed partial class StoreContractPackPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("contracts")]
    public List<ContractWeightEntry> Contracts { get; set; } = new();

    [DataField("includes")]
    public List<PackIncludeEntry> Includes { get; set; } = new();
}



[Prototype("ncContractRewardPool")]
public sealed partial class NcContractRewardPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("entries")]
    public List<ContractRewardDef> Entries { get; private set; } = new();
}




[Serializable, NetSerializable]
public enum ContractObjectiveType : byte
{
    Delivery = 0,
    Hunt = 1,
    Repair = 2,
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


