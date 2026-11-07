using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Inventory;

/// <summary>
///     Marker: adds the extra inventory slots defined by the "human_extra_pockets" template.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ExtraPocketsComponent : Component
{
}
