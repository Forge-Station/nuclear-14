using Robust.Shared.GameStates;


namespace Content.Shared._Forge.Doors.UniqueKeyDoor;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UniqueDoorKeyComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? KeyId;
}
