// Corvax-Change-Start
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.SSDIndicator;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server.Spawners.EntitySystems;

public sealed class SpawnerSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private const float SpawnBlockRange = 15f;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TimedSpawnerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            component.TimeElapsed += frameTime;

            if (component.TimeElapsed < component.IntervalSeconds)
                continue;

            var intervalsPassed = (int) (component.TimeElapsed / component.IntervalSeconds);
            component.TimeElapsed -= intervalsPassed * component.IntervalSeconds;

            for (var i = 0; i < intervalsPassed; i++)
            {
                OnTimerFired(uid, component);
            }
        }
    }

    private void OnTimerFired(EntityUid uid, TimedSpawnerComponent component)
    {
        if (ShouldBlockSpawn(uid, component))
            return;

        if (!_random.Prob(component.Chance))
            return;

        var xform = _xformQuery.GetComponent(uid);
        var coordinates = xform.Coordinates;

        var spawnCount = _random.Next(component.MinimumEntitiesSpawned, component.MaximumEntitiesSpawned + 1);
        for (var i = 0; i < spawnCount; i++)
        {
            var entity = _random.Pick(component.Prototypes);
            SpawnAtPosition(entity, coordinates);
        }
    }

    private bool ShouldBlockSpawn(EntityUid uid, TimedSpawnerComponent component)
    {
        if (component.IgnoreSpawnBlock)
            return false;

        if (!_xformQuery.TryGetComponent(uid, out var xform) || xform.MapUid == null)
            return true;

        var mapPos = _transform.GetMapCoordinates(uid, xform: xform);

        foreach (var entity in _lookup.GetEntitiesInRange(mapPos, SpawnBlockRange))
        {
            if (!Exists(entity)) continue;
            if (entity == uid) continue;

            if (TryComp(entity, out MobStateComponent? mob) &&
                (mob.CurrentState == MobState.Alive || mob.CurrentState == MobState.Critical))
            {
                if (HasComp<HumanoidAppearanceComponent>(entity))
                {
                    if (!TryComp<SSDIndicatorComponent>(entity, out var ssd) || !ssd.IsSSD)
                        return true;
                    continue;
                }

                if (TryComp(entity, out MetaDataComponent? meta) &&
                    meta.EntityPrototype?.ID is { } prototypeId &&
                    component.Prototypes.Contains(prototypeId))
                    return true;
            }

            else
                continue;
        }

        return false;
    }
}
// Corvax-Change-End
