using Robust.Shared.GameStates;
using Content.Shared.Roles.Components;

namespace Content.Shared.Roles.Components;

/// <summary>
/// Added to mind role entities to tag that they are a thief.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ThiefRoleComponent : BaseMindRoleComponent;
