using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Abilities;

/// <summary>
///     While present, this entity does not need to wield guns to fire them (dual-wielding).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AmbidextrousComponent : Component
{
}
