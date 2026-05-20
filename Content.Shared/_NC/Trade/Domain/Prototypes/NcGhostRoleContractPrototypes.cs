using Content.Shared.Customization.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
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

    [DataField("character")]
    public NcGhostRoleCharacterData Character { get; private set; } = new();

    [DataField("perks")]
    public List<ProtoId<NcGhostRolePerkPrototype>> Perks { get; private set; } = new();
}

[Prototype("ncGhostRolePerk")]
public sealed partial class NcGhostRolePerkPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("walkSpeedMultiplier")]
    public float WalkSpeedMultiplier { get; private set; } = 1f;

    [DataField("sprintSpeedMultiplier")]
    public float SprintSpeedMultiplier { get; private set; } = 1f;

    [DataField("incomingDamageMultiplier")]
    public float IncomingDamageMultiplier { get; private set; } = 1f;

    [DataField("meleeDamageMultiplier")]
    public float MeleeDamageMultiplier { get; private set; } = 1f;

    [DataField("projectileDamageMultiplier")]
    public float ProjectileDamageMultiplier { get; private set; } = 1f;

    [DataField("weaponPrototypes")]
    public List<string> WeaponPrototypes { get; private set; } = new();

    [DataField("armorItemPrototypes")]
    public List<string> ArmorItemPrototypes { get; private set; } = new();

    [DataField("armorIncomingDamageMultiplier")]
    public float ArmorIncomingDamageMultiplier { get; private set; } = 1f;

    [DataField("incomingFlatReductions")]
    public Dictionary<string, float> IncomingFlatReductions { get; private set; } = new();
}

[DataDefinition]
public sealed partial class NcGhostRoleCharacterData
{
    [DataField("name")]
    public string Name { get; set; } = string.Empty;

    [DataField("sex")]
    public Sex? Sex { get; set; }

    [DataField("gender")]
    public Gender? Gender { get; set; }

    [DataField("age")]
    public int? Age { get; set; }

    [DataField("hair")]
    public string Hair { get; set; } = string.Empty;

    [DataField("hairColor")]
    public Color? HairColor { get; set; }

    [DataField("skinColor")]
    public Color? SkinColor { get; set; }
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

[DataDefinition]
public sealed partial class NcGhostRoleSurvivalData
{
    public const int DefaultDurationSeconds = 3600;

    [DataField("durationSeconds")]
    public int DurationSeconds { get; set; } = DefaultDurationSeconds;

    [DataField("briefing")]
    public string Briefing { get; set; } = string.Empty;

    [DataField("objectiveTitle")]
    public string ObjectiveTitle { get; set; } = string.Empty;

    [DataField("objectiveDescription")]
    public string ObjectiveDescription { get; set; } = string.Empty;
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

    [DataField("survival")]
    public NcGhostRoleSurvivalData Survival { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    /// <summary>Optional extension conditions evaluated by registered server-side handlers.</summary>
    [DataField("conditions")]
    public List<ContractConditionDef> Conditions { get; private set; } = new();
}
