using Content.Shared.Access;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Doors.UniqueKeyDoor;

/// <summary>
/// Enables explicit unique-key access behavior on this door.
/// Doors can be linked to blank keys at runtime and optionally have mapper-defined key IDs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UniqueKeyDoorComponent : Component
{
    /// <summary>
    /// Shared key ID for this door.
    /// Can be pre-set in yml or created at runtime when the first blank key is linked.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public string? DoorKeyId;

    /// <summary>
    /// How many keys were linked by using blank keys on this door.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int LinkedKeyCount;

    /// <summary>
    /// Maximum number of keys that can be linked to this door by blank-key interaction.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int MaxLinkedKeys = 2;

    /// <summary>
    /// Door opens when user has any of these master key access tags.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public HashSet<ProtoId<AccessLevelPrototype>> MasterKeyTags = new();
}
