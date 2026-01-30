using Content.Server._NC.Spawner.EntitySystems;

namespace Content.Server._NC.Spawner.Components;

[RegisterComponent, Access(typeof(RespawnSpawnerSystem))]
public sealed partial class SpawnedByRespawnSpawnerComponent : Component
{
    [ViewVariables]
    public EntityUid Spawner;
}
