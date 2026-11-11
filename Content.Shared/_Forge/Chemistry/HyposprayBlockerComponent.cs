using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Chemistry;

/// <summary>
/// While this item is equipped, hypospray injections into the wearer are blocked.
/// Only affects the person wearing it; the wearer can still use a hypospray on others.
/// Used by power armor helmets.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HyposprayBlockerComponent : Component
{
}
