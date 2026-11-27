using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Roles;

/// <summary>
///     Permanent job-role bonuses applied on spawn via AddComponentSpecial.
///     Currently grants extra effective health (raises the Critical threshold)
///     and a movement speed multiplier.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RoleBonusComponent : Component
{
    [DataField, AutoNetworkedField]
    public int HealthBonus;

    [DataField, AutoNetworkedField]
    public float WalkSpeedMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintSpeedMultiplier = 1f;

    /// <summary>
    ///     Persistent tool DoAfter speed (1 = normal).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ToolSpeed = 1f;

    /// <summary>
    ///     Persistent medical DoAfter speed (1 = normal).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MedicalSpeed = 1f;

    /// <summary>
    ///     Reduces the movement slowdown from damage. 1 = no change, 0.5 = half the slowdown removed, 0 = full immunity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SlowOnDamageModifier = 1f;

    /// <summary>
    ///     Flat bonus added to GunComponent.FireRate for whitelisted weapon prototypes.
    /// </summary>
    /// <summary>
    ///     Flat bonus to maximum stamina (StaminaComponent.CritThreshold).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaBonus;

    /// <summary>
    ///     Damage multiplier applied to melee attacks made by this entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MeleeDamageMultiplier = 1f;

    /// <summary>
    ///     Multiplier for heavy melee attack stamina cost (MeleeWeaponComponent.HeavyStaminaCost).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MeleeHeavyStaminaCostMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float FireRateBonus;

    /// <summary>
    ///     Weapon prototypes that receive FireRateBonus. Empty = no weapons.
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> FireRateWhitelist = new();

    [ViewVariables]
    public FixedPoint2 BaseCritThreshold;

    [ViewVariables]
    public float BaseStaminaThreshold;
}
