using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[DataDefinition]
public sealed partial class NcRetrievalLegacySpawnTrap
{
    [DataField("enabled")] public bool Enabled { get; set; }
    [DataField("point")] public ContractPointSelectorPrototype? Point { get; set; }
    [DataField("fallbackToStore")] public bool FallbackToStore { get; set; }
    [DataField("requireSpawned")] public bool RequireSpawned { get; set; }
    [DataField("givePinpointer")] public bool GivePinpointer { get; set; }
    [DataField("pinpointerPrototype")] public string PinpointerPrototype { get; set; } = string.Empty;
    [DataField("hint")] public string Hint { get; set; } = string.Empty;
}

/// <summary>
/// Retrieval V2 Route layout: content defines cargo, route and reward.
/// Route presets define where cargo appears, where it is delivered, whether proof exists, and guidance.
/// </summary>
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

    /// <summary>Retrieval cargo. This replaces Retrieval Stage 1/2 'targets'.</summary>
    [DataField("cargo", required: true)]
    public List<NcSupplyTargetEntry> Cargo { get; private set; } = new();

    /// <summary>The route preset defines source/destination/proof/guidance. Required for Retrieval Route layout.</summary>
    [DataField("route", required: true)]
    public ProtoId<NcRetrievalRoutePresetPrototype> Route { get; private set; }

    /// <summary>Unified Retrieval rewards. Use type: Currency, Item or Pool with count.</summary>
    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    // Legacy traps. The route layout intentionally rejects the old Stage 1-4 shape.
    [DataField("targets")]
    public List<NcSupplyTargetEntry> LegacyTargets { get; private set; } = new();

    [DataField("targetCount")]
    public IntRange LegacyTargetCount { get; private set; } = IntRange.Fixed(0);

    [DataField("spawn")]
    public NcRetrievalLegacySpawnTrap? LegacySpawn { get; private set; }

    // Compatibility properties used only by older helper methods that remain in partial files.
    // Runtime and validation for Route layout must use Cargo/Route and reject these legacy fields.
    public List<NcSupplyTargetEntry> Targets => Cargo;
    public IntRange TargetCount => IntRange.Fixed(0);
    public NcRetrievalLegacySpawnTrap? Spawn => LegacySpawn;
}

