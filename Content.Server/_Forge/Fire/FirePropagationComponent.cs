namespace Content.Server._Forge.Fire;

/// <summary>
/// Explicit material configuration for tile-to-tile entity fire propagation.
/// A flammable entity without this component does not participate in N14 propagation.
/// </summary>
[RegisterComponent]
public sealed partial class FirePropagationComponent : Component
{
    /// <summary>
    /// Fire stacks sent to each orthogonally adjacent combustible tile per propagation tick.
    /// </summary>
    [DataField]
    public float SpreadFireStacks = 0.3f;

    /// <summary>
    /// Multiplier applied to incoming propagation exposure for this material.
    /// </summary>
    [DataField]
    public float IncomingMultiplier = 1f;

    /// <summary>
    /// Accumulated fire stacks required before propagation may ignite this entity.
    /// </summary>
    [DataField]
    public float IgnitionThreshold = 1f;

    /// <summary>
    /// Caps contributions from all adjacent sources during one propagation tick.
    /// </summary>
    [DataField]
    public float MaximumIncomingStacksPerTick = 0.6f;
}
