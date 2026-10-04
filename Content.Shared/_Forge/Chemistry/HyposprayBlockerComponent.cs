using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Chemistry;

/// <summary>
/// While this item is equipped, hypospray use is blocked: nobody can inject the wearer,
/// and the wearer cannot use a hypospray on anyone.
/// Used by power armor helmets.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HyposprayBlockerComponent : Component
{
}
