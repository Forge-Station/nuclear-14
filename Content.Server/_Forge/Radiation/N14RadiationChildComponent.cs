namespace Content.Server._Forge.Radiation;

/// <summary>
///     Passively reduces incoming damage based on the radiation damage currently in the body.
/// </summary>
[RegisterComponent]
public sealed partial class N14RadiationChildComponent : Component
{
    /// <summary>
    ///     Damage reduction per unit of radiation (1 rad = 0.1 reduction).
    /// </summary>
    [DataField]
    public float ConversionFactor = 0.1f;

    /// <summary>
    ///     Protection stops working when the total damage reaches this value.
    /// </summary>
    [DataField]
    public float MaxTotalDamage = 135f;
}
