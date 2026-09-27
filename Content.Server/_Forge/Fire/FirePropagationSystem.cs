using Content.Server.Atmos.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Fire;

/// <summary>
/// Propagates entity and surface fire to explicitly configured combustible entities on four neighboring tiles.
/// Surface fire is deliberately not created by this system.
/// </summary>
public sealed class FirePropagationSystem : EntitySystem
{
    private static readonly Vector2i[] NeighborOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    private static readonly TimeSpan PropagationInterval = TimeSpan.FromSeconds(1);

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly FireExposureSystem _fireExposure = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private TimeSpan _nextPropagation;

    public override void Initialize()
    {
        base.Initialize();
        _nextPropagation = _timing.CurTime + PropagationInterval;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextPropagation)
            return;

        _nextPropagation = now + PropagationInterval;
        PropagateOnce();
    }

    /// <summary>
    /// Runs one deterministic propagation step. Sources are collected before exposure is applied,
    /// so an entity ignited in this step cannot spread until the next step.
    /// </summary>
    public void PropagateOnce()
    {
        var sources = new List<(EntityUid Uid, FirePropagationComponent Propagation)>();
        var query = EntityQueryEnumerator<FlammableComponent, FirePropagationComponent>();
        while (query.MoveNext(out var uid, out var flammable, out var propagation))
        {
            if (flammable.OnFire && flammable.FireSpread)
                sources.Add((uid, propagation));
        }

        var pending = new Dictionary<EntityUid, PendingExposure>();
        foreach (var source in sources)
            CollectNeighborExposure(source.Uid, source.Propagation.SpreadFireStacks, pending);

        // Surface fire is a heat source as well as an area hazard. Keeping this bridge here means
        // combustible profiles remain the single opt-in point for walls, doors, and furniture.
        var surfaceQuery = EntityQueryEnumerator<SurfaceFireComponent>();
        while (surfaceQuery.MoveNext(out var uid, out var surfaceFire))
            CollectNeighborExposure(uid, surfaceFire.AdjacentFireStacks * surfaceFire.EffectiveIntensity, pending);

        foreach (var (target, exposure) in pending)
        {
            if (Deleted(target))
                continue;

            _fireExposure.ExposeEntity(
                target,
                exposure.Source,
                new FireExposure(
                    new(),
                    exposure.FireStacks,
                    Ignite: true,
                    ApplyDamage: false,
                    IgnitionThreshold: exposure.IgnitionThreshold));
        }
    }

    private void CollectNeighborExposure(
        EntityUid source,
        float spreadFireStacks,
        Dictionary<EntityUid, PendingExposure> pending)
    {
        var sourceTile = Transform(source).Coordinates.GetTileRef(EntityManager, _mapManager);
        if (sourceTile is not { } tile || !TryComp<MapGridComponent>(tile.GridUid, out var grid))
            return;

        foreach (var offset in NeighborOffsets)
        {
            if (!_map.TryGetTileRef(tile.GridUid, grid, tile.GridIndices + offset, out var neighbor) || neighbor.Tile.IsEmpty)
                continue;

            foreach (var target in neighbor.GetEntitiesInTile(LookupFlags.All, _lookup))
            {
                if (target == source || Deleted(target) ||
                    !TryComp<FlammableComponent>(target, out var targetFlammable) ||
                    !targetFlammable.FireSpread || targetFlammable.OnFire ||
                    !TryComp<FirePropagationComponent>(target, out var targetPropagation))
                {
                    continue;
                }

                var contribution = Math.Max(0f,
                    spreadFireStacks * targetPropagation.IncomingMultiplier);
                if (contribution <= 0f)
                    continue;

                if (pending.TryGetValue(target, out var existing))
                {
                    existing.FireStacks = Math.Min(
                        existing.FireStacks + contribution,
                        targetPropagation.MaximumIncomingStacksPerTick);
                    pending[target] = existing;
                }
                else
                {
                    pending[target] = new PendingExposure(
                        source,
                        Math.Min(contribution, targetPropagation.MaximumIncomingStacksPerTick),
                        targetPropagation.IgnitionThreshold);
                }
            }
        }
    }

    private record struct PendingExposure(EntityUid Source, float FireStacks, float IgnitionThreshold);
}
