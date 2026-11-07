namespace Content.Server._Forge.Radiation;

/// <summary>
///     Passively heals damage based on the radiation damage currently in the body.
///     Healing is applied on a fixed interval (independent of frame time).
/// </summary>
[RegisterComponent]
public sealed partial class N14RadiationChildComponent : Component
{
    /// <summary>
    ///     Healing per unit of radiation (1 rad = 0.05 healing to each type).
    /// </summary>
    [DataField]
    public float ConversionFactor = 0.05f;

    /// <summary>
    ///     Seconds between healing applications.
    /// </summary>
    [DataField]
    public float Interval = 1f;

    /// <summary>
    ///     Healing does not work when damage other than radiation is at or above this value.
    ///     Radiation itself is the fuel and is not counted.
    /// </summary>
    [DataField]
    public float MaxTotalDamage = 125f;

    /// <summary>
    ///     Next time healing should be applied.
    /// </summary>
    public TimeSpan NextHeal;
}
