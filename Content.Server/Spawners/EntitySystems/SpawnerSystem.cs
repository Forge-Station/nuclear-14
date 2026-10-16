using Content.Server.Spawners.Components;
using Content.Shared.Humanoid; // Corvax-Change
using Content.Shared.Mobs; // Corvax-Change
using Content.Shared.Mobs.Components; // Corvax-Change
using Content.Shared.SSDIndicator; // Corvax-Change
using Robust.Server.GameObjects; // Corvax-Change
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Spawners.EntitySystems;

public sealed partial class SpawnerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!; // Corvax-Change
    [Dependency] private TransformSystem _transform = default!; // Corvax-Change

    private const float SpawnBlockRange = 15f; // Corvax-Change

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TimedSpawnerComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<TimedSpawnerComponent>();
        while (query.MoveNext(out var uid, out var timedSpawner))
        {
            if (timedSpawner.NextFire > curTime)
                continue;

            OnTimerFired(uid, timedSpawner);

            timedSpawner.NextFire += timedSpawner.IntervalSeconds;
        }
    }

    private void OnMapInit(Entity<TimedSpawnerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextFire = _timing.CurTime + ent.Comp.IntervalSeconds;
    }

    private void OnTimerFired(EntityUid uid, TimedSpawnerComponent component)
    {
        if (ShouldBlockSpawn(uid, component)) // Corvax-Change
            return;

        if (!_random.Prob(component.Chance))
            return;

        var number = _random.Next(component.MinimumEntitiesSpawned, component.MaximumEntitiesSpawned);
        var coordinates = Transform(uid).Coordinates;

        for (var i = 0; i < number; i++)
        {
            var entity = _random.Pick(component.Prototypes);
            if (component.AllowContainerPlacement)
                SpawnNextToOrDrop(entity, uid);
            else
                SpawnAtPosition(entity, coordinates);
        }
    }

    // Corvax-Change-Start: do not spawn mobs right next to awake players
    private bool ShouldBlockSpawn(EntityUid uid, TimedSpawnerComponent component)
    {
        if (component.IgnoreSpawnBlock)
            return false;

        if (!TryComp<TransformComponent>(uid, out var xform) || xform.MapUid == null)
            return true;

        var mapPos = _transform.GetMapCoordinates(uid, xform: xform);

        foreach (var entity in _lookup.GetEntitiesInRange(mapPos, SpawnBlockRange))
        {
            if (entity == uid || !Exists(entity))
                continue;

            if (!TryComp<MobStateComponent>(entity, out var mob)
                || mob.CurrentState != MobState.Alive && mob.CurrentState != MobState.Critical)
                continue;

            if (HasComp<HumanoidProfileComponent>(entity))
            {
                // An SSD player does not block the spawner.
                if (!TryComp<SSDIndicatorComponent>(entity, out var ssd) || !ssd.IsSSD)
                    return true;

                continue;
            }

            // Already enough of this spawner's own mobs around.
            if (TryComp<MetaDataComponent>(entity, out var meta)
                && meta.EntityPrototype?.ID is { } prototypeId
                && component.Prototypes.Contains(prototypeId))
                return true;
        }

        return false;
    }
    // Corvax-Change-End
}
