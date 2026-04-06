using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Doors.UniqueKeyDoor;

/// <summary>
/// Stores a unique door key identifier on a physical key item.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UniqueDoorKeyComponent : Component
{
    /// <summary>
    /// Door key ID. Null means this key is a blank.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public string? KeyId;
}
