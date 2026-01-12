using Content.Server._NC.Spawner.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Spawner.Components;

[RegisterComponent, Access(typeof(RespawnSpawnerSystem))]
public sealed partial class RespawnSpawnerComponent : Component
{
    [DataField("proto", required: true)]
    public EntProtoId Prototype = string.Empty;

    [DataField("respawnSeconds")]
    public float RespawnSeconds = 300f;

    [DataField("spawnOnMapInit")]
    public bool SpawnOnMapInit = true;

    [DataField("deleteSpawnedOnShutdown")]
    public bool DeleteSpawnedOnShutdown = false;

    [ViewVariables]
    public EntityUid? Spawned;

    [ViewVariables]
    public bool RespawnScheduled;

    [ViewVariables]
    public TimeSpan NextRespawnAt;
}
