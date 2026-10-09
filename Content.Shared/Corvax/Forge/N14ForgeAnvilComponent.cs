using Robust.Shared.GameStates;

namespace Content.Shared.Corvax.Forge;

/// <summary>
///     Lives on the anvil. Tracks how many arms/legs have been forged
///     so that the first forged part is always the right one, then left one, etc.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeAnvilComponent : Component
{
    /// <summary>
    ///     Count of forged arm parts. Even index -> right arm, odd index -> left arm.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ArmsForged;

    /// <summary>
    ///     Count of forged leg parts. Even index -> right leg, odd index -> left leg.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int LegsForged;
}