using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

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


