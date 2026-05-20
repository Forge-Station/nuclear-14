using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public enum NcHuntCompletionMode : byte
{
    ConfirmedKill = 0,
    TrophyTurnIn = 1,
    BodyTurnIn = 2
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

    /// <summary>
    /// For BodyTurnIn hunts, marks the spawned target whose corpse must be brought back.
    /// </summary>
    [DataField("body")]
    public bool Body { get; set; }
}

[DataDefinition]
public sealed partial class NcHuntCompletionData
{
    [DataField("mode", required: true)]
    public NcHuntCompletionMode Mode { get; set; } = NcHuntCompletionMode.ConfirmedKill;

    [DataField("trophy")]
    public string Trophy { get; set; } = string.Empty;
}

[DataDefinition]
public sealed partial class NcHuntSpawnData
{
    [DataField("point", required: true)]
    public ContractPointSelectorPrototype Point { get; set; } = new();
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

    [DataField("targets", required: true)]
    public List<NcHuntTargetData> Targets { get; private set; } = new();

    [DataField("completion", required: true)]
    public NcHuntCompletionData Completion { get; private set; } = new();

    [DataField("spawn", required: true)]
    public NcHuntSpawnData Spawn { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    /// <summary>Optional extension conditions evaluated by registered server-side handlers.</summary>
    [DataField("conditions")]
    public List<ContractConditionDef> Conditions { get; private set; } = new();
}
