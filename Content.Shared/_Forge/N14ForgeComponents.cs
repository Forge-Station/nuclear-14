using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared.Corvax.Forge;

/// <summary>
///     Root marker for all items and stations that belong to the Legion Forge crafting chain.
///     Allows systems to easily identify all forge-related entities regardless of the concrete prototype.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeItemComponent : Component
{
}