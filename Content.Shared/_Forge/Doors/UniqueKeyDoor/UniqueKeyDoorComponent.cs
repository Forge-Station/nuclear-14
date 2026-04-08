using Content.Shared.Access;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;


namespace Content.Shared._Forge.Doors.UniqueKeyDoor;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UniqueKeyDoorComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? DoorKeyId;

    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<AccessLevelPrototype>> MasterKeyTags = new();
}
