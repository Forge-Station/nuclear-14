using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Abilities;

/// <summary>
///     Shots alternate between two allowed guns, one in each hand.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AmbidextrousComponent : Component
{
    /// <summary>
    ///     The next shot comes from the gun that is not in the active hand.
    /// </summary>
    [AutoNetworkedField]
    public bool OffhandNext;
}
