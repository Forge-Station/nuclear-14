using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public enum NcHuntCompletionMode : byte
{
    ConfirmedKill = 0,
    TrophyTurnIn = 1
}

[DataDefinition]
public sealed partial class NcHuntTargetData
{
    [DataField("group")]
    public string Group { get; set; } = string.Empty;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("count", required: true)]
    public int Count { get; set; } = 0;
}

[DataDefinition]
public sealed partial class NcHuntCompletionData
{
    [DataField("mode", required: true)]
    public NcHuntCompletionMode Mode { get; set; } = NcHuntCompletionMode.ConfirmedKill;

    [DataField("trophy")]
    public string Trophy { get; set; } = string.Empty;
}

[Prototype("ncHuntGroup")]
public sealed partial class NcHuntGroupPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("prototypes", required: true)]
    public List<string> Prototypes { get; private set; } = new();
}

[Prototype("ncHuntContract")]
public sealed partial class NcHuntContractPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("target", required: true)]
    public NcHuntTargetData Target { get; private set; } = new();

    [DataField("completion", required: true)]
    public NcHuntCompletionData Completion { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    // Legacy traps for old storeContract hunt shape.
    [DataField("targetItem")]
    public string LegacyTargetItem { get; set; } = string.Empty;

    [DataField("required")]
    public IntRange LegacyRequired { get; set; } = IntRange.Fixed(int.MinValue);

    [DataField("match")]
    public PrototypeMatchMode? LegacyMatchMode { get; set; }

    [DataField("objectiveType")]
    public ContractObjectiveType? LegacyObjectiveType { get; set; }

    [DataField("runtime")]
    public StoreContractRuntimePrototype? LegacyRuntime { get; set; }

    [DataField("targets")]
    public List<StoreContractTargetEntry>? LegacyTargets { get; set; }

    [DataField("targetCount")]
    public IntRange LegacyTargetCount { get; set; } = IntRange.Fixed(int.MinValue);
}
