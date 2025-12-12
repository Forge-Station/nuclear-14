using Robust.Shared.GameStates;
using Content.Shared.Storage;

namespace Content.Shared._NC.SpawnWhenOpened;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SpawnWhenOpenedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsAlreadyOpened = false;

    /// <summary>
    /// Can you spawn prototype if you open object multiple times
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsRepeatable = false;

    /// <summary>
    /// The list of prototypes that will spawn when object opened
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntitySpawnEntry> Prototypes = new();
}
