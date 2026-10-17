using Robust.Shared.GameStates;

namespace Content.Shared.Warps;

/// <summary>
/// Allows ghosts etc to warp to this entity by name.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WarpPointComponent : Component
{
    /// <summary>
    /// Forge-Change: идентификатор, по которому варпер ищет точку.
    /// </summary>
    [DataField]
    public string ID { get; set; } = string.Empty;

    [DataField]
    public LocId? Location;

    /// <summary>
    /// If true, ghosts warping to this entity will begin following it.
    /// </summary>
    [DataField]
    public bool Follow;
}
