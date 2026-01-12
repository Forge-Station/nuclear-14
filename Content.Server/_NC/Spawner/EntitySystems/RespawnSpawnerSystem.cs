using System.Numerics;
using Content.Server._NC.Spawner.Components;
using Content.Server.Ghost;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._NC.Spawner.EntitySystems;

public sealed class RespawnSpawnerSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    private const int MaxSpawnsPerTick = 10;
    private const float SpawnCheckSize = 0.8f;
    private const double BlockedRetrySeconds = 10.0;
    private readonly PriorityQueue<EntityUid, long> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RespawnSpawnerMarkerComponent, MapInitEvent>(OnMarkerMapInit);
        SubscribeLocalEvent<RespawnSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RespawnSpawnerComponent, ComponentShutdown>(OnSpawnerShutdown);
        SubscribeLocalEvent<SpawnedByRespawnSpawnerComponent, EntityTerminatingEvent>(OnSpawnedTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        var now = _timing.CurTime;
        var nowTicks = now.Ticks;
        var processed = 0;

        while (processed < MaxSpawnsPerTick &&
               _pending.TryPeek(out var uid, out var dueTicks) &&
               dueTicks <= nowTicks)
        {
            _pending.Dequeue();

            if (ProcessSpawner(uid, dueTicks, now))
            {
                processed++;
            }
        }
    }

    private bool ProcessSpawner(EntityUid uid, long dueTicks, TimeSpan now)
    {
        if (!TryComp(uid, out RespawnSpawnerComponent? comp))
            return false;

        if (!comp.RespawnScheduled)
            return false;

        if (comp.NextRespawnAt.Ticks != dueTicks)
            return false;

        var xform = Transform(uid);
        if (xform.MapID == MapId.Nullspace)
        {
            RemovePending(comp);
            return false;
        }

        if (IsTileBlocked(uid, xform))
        {
            comp.NextRespawnAt = now + TimeSpan.FromSeconds(BlockedRetrySeconds);
            _pending.Enqueue(uid, comp.NextRespawnAt.Ticks);

            return true;
        }
        SpawnNow(uid, comp);
        RemovePending(comp);

        return true;
    }

    private void OnMarkerMapInit(EntityUid uid, RespawnSpawnerMarkerComponent marker, MapInitEvent args)
    {
        if (!TryComp(uid, out RespawnSpawnerComponent? cfg))
            return;

        var xform = Transform(uid);
        if (!xform.Coordinates.IsValid(EntityManager))
        {
            QueueDel(uid);
            return;
        }

        var runtimeUid = Spawn(marker.RuntimePrototype, xform.Coordinates);
        var runtimeComp = EnsureComp<RespawnSpawnerComponent>(runtimeUid);

        runtimeComp.Prototype = cfg.Prototype;
        runtimeComp.RespawnSeconds = cfg.RespawnSeconds;
        runtimeComp.DeleteSpawnedOnShutdown = cfg.DeleteSpawnedOnShutdown;
        runtimeComp.SpawnOnMapInit = false;

        QueueDel(uid);

        if (cfg.SpawnOnMapInit)
            EnsureSpawned(runtimeUid, runtimeComp);
    }

    private void OnMapInit(EntityUid uid, RespawnSpawnerComponent comp, MapInitEvent args)
    {
        if (HasComp<RespawnSpawnerMarkerComponent>(uid))
            return;

        if (comp.SpawnOnMapInit)
            EnsureSpawned(uid, comp);
    }

    private void OnSpawnerShutdown(EntityUid uid, RespawnSpawnerComponent comp, ComponentShutdown args)
    {
        RemovePending(comp);

        if (!comp.DeleteSpawnedOnShutdown)
            return;

        if (comp.Spawned is { } spawned && !TerminatingOrDeleted(spawned))
            QueueDel(spawned);
    }

    private void OnSpawnedTerminating(EntityUid uid, SpawnedByRespawnSpawnerComponent marker, ref EntityTerminatingEvent args)
    {
        var spawner = marker.Spawner;

        if (TerminatingOrDeleted(spawner) || !TryComp(spawner, out RespawnSpawnerComponent? spawnerComp))
            return;

        if (spawnerComp.Spawned != uid)
            return;

        spawnerComp.Spawned = null;
        ScheduleRespawn(spawner, spawnerComp);
    }

    private bool IsTileBlocked(EntityUid spawner, TransformComponent xform)
    {
        var origin = _transform.GetWorldPosition(xform);
        var box = Box2.CenteredAround(origin, new Vector2(SpawnCheckSize, SpawnCheckSize));

        foreach (var ent in _lookup.GetEntitiesIntersecting(xform.MapID, box))
        {
            if (ent == spawner)
                continue;
            if (HasComp<GhostComponent>(ent) || HasComp<ObserverRoleComponent>(ent))
                continue;
            if (HasComp<MobStateComponent>(ent))
                return true;
            if (TryComp<PhysicsComponent>(ent, out var phys) && phys.CanCollide && phys.Hard)
                return true;
        }

        return false;
    }

    private void EnsureSpawned(EntityUid spawner, RespawnSpawnerComponent comp)
    {
        if (comp.Spawned is { } existing && !TerminatingOrDeleted(existing))
            return;
        RemovePending(comp);

        SpawnNow(spawner, comp);
    }

    private void SpawnNow(EntityUid spawner, RespawnSpawnerComponent comp)
    {
        if (string.IsNullOrWhiteSpace(comp.Prototype))
            return;

        var coords = Transform(spawner).Coordinates;
        if (!coords.IsValid(EntityManager))
            return;

        var spawned = Spawn(comp.Prototype, coords);

        var marker = EnsureComp<SpawnedByRespawnSpawnerComponent>(spawned);
        marker.Spawner = spawner;

        comp.Spawned = spawned;
    }

    private void ScheduleRespawn(EntityUid spawner, RespawnSpawnerComponent comp)
    {
        if (comp.RespawnScheduled)
            return;

        var delay = TimeSpan.FromSeconds(Math.Max(0f, comp.RespawnSeconds));
        comp.NextRespawnAt = _timing.CurTime + delay;
        comp.RespawnScheduled = true;

        _pending.Enqueue(spawner, comp.NextRespawnAt.Ticks);
    }

    private static void RemovePending(RespawnSpawnerComponent comp)
    {
        comp.RespawnScheduled = false;
        comp.NextRespawnAt = default;
    }
}
