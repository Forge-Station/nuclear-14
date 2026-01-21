using Content.Server._NC.Spawner.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Spawner.Components;

[RegisterComponent, Access(typeof(RespawnSpawnerSystem))]
public sealed partial class RespawnSpawnerMarkerComponent : Component
{
    [DataField("runtimePrototype")]
    public EntProtoId RuntimePrototype = "NcRespawnSpawnerRuntime";
}
