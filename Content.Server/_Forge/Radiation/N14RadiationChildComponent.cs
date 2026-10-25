namespace Content.Server._Forge.Radiation;

/// <summary>
///     Passively heals damage based on the radiation damage currently in the body.
///     Healing is applied on a fixed interval (independent of frame time).
/// </summary>
[RegisterComponent]
public sealed partial class N14RadiationChildComponent : Component
{
    /// <summary>
    ///     Healing per unit of radiation (1 rad = 0.1 healing to each type).
    /// </summary>
    [DataField]
    public float ConversionFactor = 0.1f;

    /// <summary>
    ///     Seconds between healing applications.
    /// </summary>
    [DataField]
    public float Interval = 1f;

    /// <summary>
    ///     Healing does not work above this total damage.
    /// </summary>
    [DataField]
    public float MaxTotalDamage = 140f;

    /// <summary>
    ///     Next time healing should be applied.
    /// </summary>
    public TimeSpan NextHeal;
}
