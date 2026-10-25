using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Nutrition;

/// <summary>
///     Scales how fast the entity's hunger and thirst decay.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ConsumptionModifierComponent : Component
{
    /// <summary>
    ///     Multiplier for hunger decay: 1 = normal, 0.75 = 25% slower, 1.25 = 25% faster.
    /// </summary>
    [DataField]
    public float HungerMultiplier = 1f;

    /// <summary>
    ///     Multiplier for thirst decay: 1 = normal, 0.8 = 20% slower, 1.25 = 25% faster.
    /// </summary>
    [DataField]
    public float ThirstMultiplier = 1f;
}
