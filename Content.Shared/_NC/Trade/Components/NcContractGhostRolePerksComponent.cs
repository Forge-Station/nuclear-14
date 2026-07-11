using Content.Shared.Damage;
using Robust.Shared.GameStates;


namespace Content.Shared._NC.Trade;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState,]
public sealed partial class NcContractGhostRolePerksComponent : Component
{
    [DataField("armorIncomingDamageMultiplier"), AutoNetworkedField,]
    public float ArmorIncomingDamageMultiplier = 1f;

    [DataField("armorItemPrototypes")]
    public List<string> ArmorItemPrototypes = new();

    [DataField("incomingDamageMultiplier"), AutoNetworkedField,]
    public float IncomingDamageMultiplier = 1f;

    [DataField("incomingFlatReductions")]
    public Dictionary<string, float> IncomingFlatReductions = new();

    [DataField("meleeDamageMultiplier"), AutoNetworkedField,]
    public float MeleeDamageMultiplier = 1f;

    [DataField("perkIds")]
    public List<string> PerkIds = new();

    [DataField("projectileDamageMultiplier"), AutoNetworkedField,]
    public float ProjectileDamageMultiplier = 1f;

    [DataField("passiveHealing"), AutoNetworkedField,]
    public DamageSpecifier PassiveHealing = new();

    [DataField("passiveHealingInterval"), AutoNetworkedField,]
    public float PassiveHealingInterval = 1f;

    public TimeSpan NextPassiveHealing = TimeSpan.Zero;

    [DataField("sprintSpeedMultiplier"), AutoNetworkedField,]
    public float SprintSpeedMultiplier = 1f;

    [DataField("walkSpeedMultiplier"), AutoNetworkedField,]
    public float WalkSpeedMultiplier = 1f;

    [DataField("weaponPrototypes")]
    public List<string> WeaponPrototypes = new();
}
