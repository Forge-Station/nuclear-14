using Content.Shared.Customization.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Prototype("ncGhostRolePreset")]
public sealed partial class NcGhostRolePresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("entityPrototype", required: true)]
    public string EntityPrototype { get; private set; } = string.Empty;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("rules")]
    public string Rules { get; private set; } = string.Empty;

    [DataField("requirements")]
    public List<CharacterRequirement> Requirements { get; private set; } = new();
}

[DataDefinition]
public sealed partial class NcGhostRoleSpawnData
{
    [DataField("point", required: true)]
    public ContractPointSelectorPrototype Point { get; set; } = new();

    [DataField("acceptTimeoutSeconds")]
    public int AcceptTimeoutSeconds { get; set; } = 300;
}

[DataDefinition]
public sealed partial class NcGhostRoleCompletionData
{
    [DataField("mode", required: true)]
    public NcGhostRoleCompletionMode Mode { get; set; } = NcGhostRoleCompletionMode.DeadBodyTurnIn;
}

[Serializable, NetSerializable]
public enum NcGhostRoleCompletionMode : byte
{
    DeadBodyTurnIn = 0,
    AliveCuffedTurnIn = 1
}

[Prototype("ncGhostRoleContract")]
public sealed partial class NcGhostRoleContractPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    [DataField("role", required: true)]
    public ProtoId<NcGhostRolePresetPrototype> Role;

    [DataField("spawn", required: true)]
    public NcGhostRoleSpawnData Spawn { get; private set; } = new();

    [DataField("completion", required: true)]
    public NcGhostRoleCompletionData Completion { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();
}
