// Corvax-Change-Start
using System.Threading;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

[RegisterComponent, EntityCategory("Spawner")]
public sealed partial class TimedSpawnerComponent : Component
{
    [DataField] public List<EntProtoId> Prototypes = [];
    [DataField] public float Chance = 1.0f;
    [DataField] public int IntervalSeconds = 60;
    [DataField] public int MinimumEntitiesSpawned = 1;
    [DataField] public int MaximumEntitiesSpawned = 1;
    /// <summary>
    /// Необходима для спавнеров какашек.
    /// </summary>
    [DataField] public bool IgnoreSpawnBlock = false;
    public float TimeElapsed;
}
// Corvax-Change-End
