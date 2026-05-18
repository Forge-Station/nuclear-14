using Robust.Shared.GameStates;

namespace Content.Shared._NC.Trade;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NcContractGhostRolePerksComponent : Component
{
    [DataField("perkIds")]
    public List<string> PerkIds = new();

    [DataField("walkSpeedMultiplier"), AutoNetworkedField]
    public float WalkSpeedMultiplier = 1f;

    [DataField("sprintSpeedMultiplier"), AutoNetworkedField]
    public float SprintSpeedMultiplier = 1f;

    [DataField("incomingDamageMultiplier"), AutoNetworkedField]
    public float IncomingDamageMultiplier = 1f;

    [DataField("meleeDamageMultiplier"), AutoNetworkedField]
    public float MeleeDamageMultiplier = 1f;

    [DataField("projectileDamageMultiplier"), AutoNetworkedField]
    public float ProjectileDamageMultiplier = 1f;

    [DataField("weaponPrototypes")]
    public List<string> WeaponPrototypes = new();

    [DataField("armorItemPrototypes")]
    public List<string> ArmorItemPrototypes = new();

    [DataField("armorIncomingDamageMultiplier"), AutoNetworkedField]
    public float ArmorIncomingDamageMultiplier = 1f;

    [DataField("incomingFlatReductions")]
    public Dictionary<string, float> IncomingFlatReductions = new();
}
