using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Quench;

/// <summary>
///     Heated marker applied when a plate/part is heated in the gorn.
///     Clicking a heated item on water forges it (forged variants).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeHeatedComponent : Component
{
}

/// <summary>
///     Marks a forged (закалённый) plate part produced by quenching a heated plate in water.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeForgedComponent : Component
{
}