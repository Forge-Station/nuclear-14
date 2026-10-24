namespace Content.Server._Forge.Radiation;

/// <summary>
///     Heals physical/mechanical damage while the entity has radiation damage accumulated.
///     Radiation damage itself is not removed.
/// </summary>
[RegisterComponent]
public sealed partial class N14RadiationChildComponent : Component
{
    /// <summary>
    ///     Radiation damage processed (and healed) per second.
    /// </summary>
    [DataField]
    public float RadsPerSecond = 5f;

    /// <summary>
    ///     Healing per unit of radiation damage.
    /// </summary>
    [DataField]
    public float HealPerTrigger = 0.1f;

    /// <summary>
    ///     Healing stops when the total damage reaches this value.
    /// </summary>
    [DataField]
    public float MaxTotalDamage = 135f;
}
