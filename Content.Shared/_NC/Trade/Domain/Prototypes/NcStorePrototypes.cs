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
public sealed partial class NcBarterCostEntry
{
    /// <summary>Exact entity prototype the player must give.</summary>
    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    /// <summary>ncItemGroup id. Groups are valid only for checking existing player items.</summary>
    [DataField("group")]
    public string Group { get; set; } = string.Empty;

    /// <summary>Stack currency id the player must pay.</summary>
    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    [DataField("count")]
    public int Count { get; set; } = 1;
}

[DataDefinition, Serializable, NetSerializable]
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

[DataDefinition, Serializable, NetSerializable]
public sealed partial class NcBarterReceivePoolEntry
{
    /// <summary>Weighted reward pool id. Uses ncContractRewardPool entries for now.</summary>
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
    [IdDataField]
    public string ID { get; private set; } = default!;

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
}

[Prototype("ncBarterCategory")]
public sealed partial class NcBarterCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>References to standalone ncBarterListing prototypes.</summary>
    [DataField("listings", required: true)]
    public List<ProtoId<NcBarterListingPrototype>> Listings { get; private set; } = new();
}

[Prototype("ncBarterPreset")]
public sealed partial class NcBarterPresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("categories", required: true)]
    public List<ProtoId<NcBarterCategoryPrototype>> Categories { get; private set; } = new();
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

    /// <summary>
    /// ContractsV2 packs. Kept separate from legacy packs so new contract families can be migrated
    /// one by one without changing the existing storeContract/storeContractPack format.
    /// </summary>
    [DataField("packsV2")]
    public List<PackIncludeEntry> PacksV2 { get; set; } = new();

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

/// <summary>
/// ContractsV2 pack. This intentionally does not reuse storeContractPack.contracts: each V2 family
/// gets its own list, so supply/retrieval/courier/bounty/wanted can be migrated independently.
/// </summary>
[Prototype("ncContractPackV2")]
public sealed partial class NcContractPackV2Prototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("supply")]
    public List<ContractWeightEntry> Supply { get; set; } = new();

    /// <summary>
    /// Retrieval V2 contracts. These use the same strict targets/reward/count grammar as Supply V2,
    /// but are kept as a separate contract family so retrieval/delivery semantics can evolve independently.
    /// </summary>
    [DataField("retrieval")]
    public List<ContractWeightEntry> Retrieval { get; set; } = new();

    [DataField("includes")]
    public List<PackIncludeEntry> Includes { get; set; } = new();
}

/// <summary>
/// ContractsV2 item group. Groups are only valid for checking already existing turn-in items.
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

[DataDefinition]
public sealed partial class NcSupplyTargetEntry
{
    /// <summary>Exact entity prototype required for turn-in.</summary>
    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    /// <summary>ncItemGroup id. Groups are matched like legacy matchers, but only for turn-in.</summary>
    [DataField("group")]
    public string Group { get; set; } = string.Empty;

    /// <summary>Required amount. If this is a range, it is rolled once when the contract is generated.</summary>
    [DataField("count", required: true)]
    public IntRange Count { get; set; } = IntRange.Fixed(0);

    /// <summary>Used only when targetCount is configured. Larger weight means this target is picked more often.</summary>
    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

/// <summary>
/// ContractsV2 Supply reward entry. Unified format:
/// reward:
/// - type: Currency / Item / Pool
///   currency/prototype/pool: ...
///   count: 1 or { min, max }
/// </summary>
[DataDefinition]
public sealed partial class NcSupplyRewardEntry
{
    [DataField("type", required: true)]
    public StoreRewardType Type { get; set; } = StoreRewardType.Unspecified;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    [DataField("pool")]
    public string Pool { get; set; } = string.Empty;

    [DataField("count", required: true)]
    public IntRange Count { get; set; } = IntRange.Fixed(0);

    /// <summary>Legacy trap: Supply V2 reward entries must use count, not amount.</summary>
    [DataField("amount")]
    public IntRange LegacyAmount { get; set; } = IntRange.Fixed(int.MinValue);

    /// <summary>Legacy trap: Supply V2 uses pool weight / count range, not per-entry probability.</summary>
    [DataField("prob")]
    public float LegacyProbability { get; set; } = float.NaN;

    /// <summary>Legacy trap: Supply V2 uses pool weight / count range, not per-entry chance.</summary>
    [DataField("chance")]
    public float LegacyChance { get; set; } = float.NaN;

    /// <summary>Legacy trap: use prototype/currency/pool depending on type, not id.</summary>
    [DataField("id")]
    public string LegacyId { get; set; } = string.Empty;

    /// <summary>Legacy trap: nested option lists are not part of Supply V2 rewards.</summary>
    [DataField("options")]
    public List<ContractRewardDef>? LegacyOptions { get; set; }
}

/// <summary>
/// ContractsV2 Supply: the player brings already existing items and turns them in through
/// the current server-authoritative claim/reward flow. No runtime, no spawning, no prediction.
/// </summary>
[Prototype("ncSupplyContract")]
public sealed partial class NcSupplyContractPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("difficulty")]
    public string Difficulty { get; private set; } = "Easy";

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    /// <summary>Optional entity prototype id used only as a UI icon fallback for the contract card.</summary>
    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    /// <summary>Supply targets. If targetCount is absent, all targets are required. If targetCount is set, a weighted subset is picked on generation.</summary>
    [DataField("targets", required: true)]
    public List<NcSupplyTargetEntry> Targets { get; private set; } = new();

    /// <summary>Optional number of targets to pick from the targets pool. Fixed 0 means unset / require all targets.</summary>
    [DataField("targetCount", required: false)]
    public IntRange TargetCount { get; private set; } = IntRange.Fixed(0);

    /// <summary>Unified Supply rewards. Use type: Currency, Item or Pool with count.</summary>
    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();
}



/// <summary>
/// Retrieval V2 optional spawn source. Stage 2 uses existing NcContractSpawnPoint markers
/// through ContractPointSelectorPrototype. It spawns generic target items when the contract is taken;
/// claim still accepts any matching item. Tracked-item identity is intentionally a later stage.
/// </summary>
[DataDefinition]
public sealed partial class NcRetrievalSpawnPrototype
{
    /// <summary>If false, this block is ignored and the contract behaves like Stage 1 generic retrieval.</summary>
    [DataField("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Where retrieval target items should be spawned. Uses NcContractSpawnPoint markers.</summary>
    [DataField("point")]
    public ContractPointSelectorPrototype? Point { get; set; }

    /// <summary>Allow fallback to store coordinates when no configured marker is found. Keep false for real content.</summary>
    [DataField("fallbackToStore")]
    public bool FallbackToStore { get; set; }
}


[Prototype("ncRetrievalContract")]
public sealed partial class NcRetrievalContractPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("difficulty")]
    public string Difficulty { get; private set; } = "Easy";

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    /// <summary>Optional entity prototype id used only as a UI icon fallback for the contract card.</summary>
    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    /// <summary>
    /// Retrieval targets. Stage 1 means the player brings already existing items to the trader.
    /// If targetCount is absent, all targets are required. If set, a weighted subset is picked on generation.
    /// </summary>
    [DataField("targets", required: true)]
    public List<NcSupplyTargetEntry> Targets { get; private set; } = new();

    /// <summary>Optional number of targets to pick from the targets pool. Fixed 0 means unset / require all targets.</summary>
    [DataField("targetCount", required: false)]
    public IntRange TargetCount { get; private set; } = IntRange.Fixed(0);

    /// <summary>Unified Retrieval rewards. Use type: Currency, Item or Pool with count.</summary>
    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    /// <summary>
    /// Optional Stage 2 spawn source. When configured, target items are spawned at a selected marker
    /// when the contract is taken. Claim remains generic and does not require a specific spawned entity.
    /// </summary>
    [DataField("spawn")]
    public NcRetrievalSpawnPrototype? Spawn { get; private set; }
}



/// <summary>
/// Legacy/general reward pool used by old storeContract rewards and Barter receivePools.
/// Keep this format tolerant because legacy contracts may still use id/amount/prob/options.
/// Supply V2 must use ncSupplyRewardPool instead.
/// </summary>
[Prototype("ncContractRewardPool")]
public sealed partial class NcContractRewardPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("entries")]
    public List<ContractRewardDef> Entries { get; private set; } = new();
}

/// <summary>
/// Strict Supply V2 reward pool. This format intentionally has no legacy id/amount/prob/chance/options fields.
/// Use count + weight only; nested pools are rejected by Supply validation.
/// </summary>
[Prototype("ncSupplyRewardPool")]
public sealed partial class NcSupplyRewardPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("entries", required: true)]
    public List<NcSupplyRewardPoolEntry> Entries { get; private set; } = new();
}

[DataDefinition]
public sealed partial class NcSupplyRewardPoolEntry
{
    [DataField("type", required: true)]
    public StoreRewardType Type { get; set; } = StoreRewardType.Unspecified;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    [DataField("count", required: true)]
    public IntRange Count { get; set; } = IntRange.Fixed(0);

    [DataField("weight")]
    public int Weight { get; set; } = 1;

    [DataField("max")]
    public int MaxRepeats { get; set; } = 0;

    /// <summary>Legacy trap: Supply V2 reward pool entries must use count, not amount.</summary>
    [DataField("amount")]
    public IntRange LegacyAmount { get; set; } = IntRange.Fixed(int.MinValue);

    /// <summary>Legacy trap: Supply V2 reward pools use weight, not prob.</summary>
    [DataField("prob")]
    public float LegacyProbability { get; set; } = float.NaN;

    /// <summary>Legacy trap: Supply V2 reward pools use weight, not chance.</summary>
    [DataField("chance")]
    public float LegacyChance { get; set; } = float.NaN;

    /// <summary>Legacy trap: use prototype/currency depending on type, not id.</summary>
    [DataField("id")]
    public string LegacyId { get; set; } = string.Empty;

    /// <summary>Legacy trap: nested pools are rejected explicitly by Supply V2 validation.</summary>
    [DataField("pool")]
    public string LegacyPool { get; set; } = string.Empty;

    /// <summary>Legacy trap: nested option lists are not part of Supply V2 reward pools.</summary>
    [DataField("options")]
    public List<ContractRewardDef>? LegacyOptions { get; set; }
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
