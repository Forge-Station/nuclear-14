using Content.Shared.Maps;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.Fire;

/// <summary>
/// Tracks fast flamethrower projectiles every server tick and fills the grid tiles crossed between
/// their previous and current positions. Surface-fire lifetime and effects are processed separately
/// by <see cref="SurfaceFireSystem"/> at a fixed interval.
/// </summary>
public sealed class SurfaceFireTrailSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SurfaceFireSystem _surfaceFire = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurfaceFireTrailComponent, MapInitEvent>(OnTrailMapInit);
        SubscribeLocalEvent<SurfaceFireTrailComponent, ProjectileHitEvent>(OnTrailProjectileHit);
    }

    private void OnTrailMapInit(Entity<SurfaceFireTrailComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.RemainingTilesToSkip = Math.Max(0, ent.Comp.TilesToSkip);
    }

    private void OnTrailProjectileHit(Entity<SurfaceFireTrailComponent> ent, ref ProjectileHitEvent args)
    {
        var coordinates = Transform(args.Target).Coordinates;
        if (!IsPastSafeDistance(args.Shooter, coordinates, ent.Comp.TilesToSkip))
            return;
        if (!IsWithinTrailRange(args.Shooter, coordinates, ent.Comp.TilesToSkip, ent.Comp.MaxTiles))
            return;

        _surfaceFire.TrySpawnFire(
            coordinates,
            ent.Comp.FirePrototype,
            ent.Comp.Duration,
            ent.Comp.Intensity,
            args.Shooter,
            out _);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SurfaceFireTrailComponent>();
        while (query.MoveNext(out var uid, out var trail))
        {
            PaintTrail((uid, trail));
        }
    }

    private void PaintTrail(Entity<SurfaceFireTrailComponent> trail)
    {
        if (Deleted(trail))
            return;

        var coordinates = Transform(trail).Coordinates;
        var tile = coordinates.GetTileRef(EntityManager, _mapManager);
        if (tile is not { } current || current.Tile.IsEmpty)
            return;

        var source = TryComp<ProjectileComponent>(trail, out var projectile) ? projectile.Shooter : null;
        if (!trail.Comp.HasLastTile)
        {
            var sourceTile = source is { } shooter && Exists(shooter)
                ? Transform(shooter).Coordinates.GetTileRef(EntityManager, _mapManager)
                : null;

            trail.Comp.LastGrid = sourceTile?.GridUid ?? current.GridUid;
            trail.Comp.LastTile = sourceTile?.GridIndices ?? current.GridIndices;
            trail.Comp.HasLastTile = true;
        }

        if (trail.Comp.LastGrid != current.GridUid)
        {
            trail.Comp.LastGrid = current.GridUid;
            trail.Comp.LastTile = current.GridIndices;
            return;
        }

        if (trail.Comp.LastTile == current.GridIndices)
            return;

        if (!TryComp<MapGridComponent>(current.GridUid, out var grid))
            return;

        var start = trail.Comp.LastTile;
        var end = current.GridIndices;
        var x = start.X;
        var y = start.Y;
        var dx = Math.Abs(end.X - start.X);
        var sx = start.X < end.X ? 1 : -1;
        var dy = -Math.Abs(end.Y - start.Y);
        var sy = start.Y < end.Y ? 1 : -1;
        var error = dx + dy;

        // Bresenham traversal prevents fast projectiles from leaving gaps in the flame trail.
        while (x != end.X || y != end.Y)
        {
            var twiceError = 2 * error;
            if (twiceError >= dy)
            {
                error += dy;
                x += sx;
            }

            if (twiceError <= dx)
            {
                error += dx;
                y += sy;
            }

            if (!_map.TryGetTileRef(current.GridUid, grid, new Vector2i(x, y), out var crossed) || crossed.Tile.IsEmpty)
                break;

            if (trail.Comp.RemainingTilesToSkip > 0)
            {
                trail.Comp.RemainingTilesToSkip--;
                if (_surfaceFire.TileBlocksFire(crossed))
                {
                    QueueDel(trail.Owner);
                    break;
                }

                continue;
            }

            if (trail.Comp.MaxTiles > 0 && trail.Comp.PaintedTiles >= trail.Comp.MaxTiles)
            {
                QueueDel(trail.Owner);
                break;
            }

            SpawnTrailTile(crossed, trail.Comp, source, trail.Owner);
            trail.Comp.PaintedTiles++;
            if (_surfaceFire.TileBlocksFire(crossed))
                break;
            if (trail.Comp.MaxTiles > 0 && trail.Comp.PaintedTiles >= trail.Comp.MaxTiles)
            {
                QueueDel(trail.Owner);
                break;
            }
        }

        trail.Comp.LastTile = current.GridIndices;
    }

    private bool IsPastSafeDistance(EntityUid? source, EntityCoordinates target, int tilesToSkip)
    {
        if (tilesToSkip <= 0 || source is not { } shooter || !Exists(shooter))
            return true;

        var sourceTile = Transform(shooter).Coordinates.GetTileRef(EntityManager, _mapManager);
        var targetTile = target.GetTileRef(EntityManager, _mapManager);
        if (sourceTile is not { } from || targetTile is not { } to || from.GridUid != to.GridUid)
            return true;

        var distance = Math.Max(
            Math.Abs(to.GridIndices.X - from.GridIndices.X),
            Math.Abs(to.GridIndices.Y - from.GridIndices.Y));
        return distance > tilesToSkip;
    }

    private bool IsWithinTrailRange(EntityUid? source, EntityCoordinates target, int tilesToSkip, int maxTiles)
    {
        if (maxTiles <= 0 || source is not { } shooter || !Exists(shooter))
            return true;

        var sourceTile = Transform(shooter).Coordinates.GetTileRef(EntityManager, _mapManager);
        var targetTile = target.GetTileRef(EntityManager, _mapManager);
        if (sourceTile is not { } from || targetTile is not { } to || from.GridUid != to.GridUid)
            return true;

        var distance = Math.Max(
            Math.Abs(to.GridIndices.X - from.GridIndices.X),
            Math.Abs(to.GridIndices.Y - from.GridIndices.Y));
        return distance <= Math.Max(0, tilesToSkip) + maxTiles;
    }

    private void SpawnTrailTile(
        TileRef tile,
        SurfaceFireTrailComponent trail,
        EntityUid? source,
        EntityUid trailUid)
    {
        _surfaceFire.TrySpawnFire(
            _map.ToCenterCoordinates(tile),
            trail.FirePrototype,
            trail.Duration,
            trail.Intensity,
            source,
            out _);

        if (_surfaceFire.TileBlocksFire(tile))
            QueueDel(trailUid);
    }
}
