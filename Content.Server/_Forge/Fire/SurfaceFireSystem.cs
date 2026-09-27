using System.Linq;
using System.Numerics;
using Content.Shared._Forge.Fire;
using Content.Server.Chemistry.TileReactions;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Fire;

public sealed class SurfaceFireSystem : EntitySystem
{
    private readonly record struct PendingFuelIgnition(
        EntityCoordinates Coordinates,
        EntProtoId Prototype,
        TimeSpan Duration,
        float Intensity,
        EntityUid? Source);

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly Vector2i[] FuelSpreadOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    [Dependency] private readonly FireExposureSystem _fireExposure = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SolutionContainerSystem _solution = default!;
    [Dependency] private readonly SurfaceVaporSystem _vapor = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurfaceFireComponent, MapInitEvent>(OnFireMapInit);
        SubscribeLocalEvent<SurfaceFireTrailComponent, MapInitEvent>(OnTrailMapInit);
        SubscribeLocalEvent<SurfaceFireTrailComponent, ProjectileHitEvent>(OnTrailProjectileHit);
        SubscribeLocalEvent<SurfaceFireOnTriggerComponent, TriggerEvent>(OnFirePatternTrigger);
    }

    private void OnFireMapInit(Entity<SurfaceFireComponent> ent, ref MapInitEvent args)
    {
        var now = _timing.CurTime;
        ent.Comp.SpawnedAt = now;
        ent.Comp.ExpiresAt = now + ent.Comp.Duration;
        ent.Comp.NextTick = now;
        ent.Comp.NextVaporEmission = now + ent.Comp.VaporEmissionInterval;
        ent.Comp.NextFuelSpread = now + ent.Comp.FuelSpreadInterval;
        UpdateAppearance(ent, now);
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

        TrySpawnFire(
            coordinates,
            ent.Comp.FirePrototype,
            ent.Comp.Duration,
            ent.Comp.Intensity,
            args.Shooter,
            out _);
    }

    private void OnFirePatternTrigger(Entity<SurfaceFireOnTriggerComponent> ent, ref TriggerEvent args)
    {
        var direction = TryComp<PhysicsComponent>(ent, out var physics) &&
                        !physics.LinearVelocity.IsLengthZero()
            ? physics.LinearVelocity
            : Transform(ent).LocalRotation.ToWorldVec();
        var source = args.User;
        if (source == null && TryComp<ProjectileComponent>(ent, out var projectile))
            source = projectile.Shooter;

        SpawnPattern(Transform(ent).Coordinates, direction, ent.Comp, source);
    }

    /// <summary>
    /// Creates a surface-fire pattern from data supplied by a source component.
    /// Returns the number of tiles that accepted fire (including refreshed existing fire).
    /// </summary>
    public int SpawnPattern(
        EntityCoordinates origin,
        Vector2 direction,
        SurfaceFireOnTriggerComponent pattern,
        EntityUid? source = null)
    {
        var originTile = origin.GetTileRef(EntityManager, _mapManager);
        if (originTile is not { } center ||
            !TryComp<MapGridComponent>(center.GridUid, out var grid))
        {
            return 0;
        }

        var count = 0;
        foreach (var offset in GetPatternOffsets(pattern, direction))
        {
            var indices = center.GridIndices + offset;
            if (!_map.TryGetTileRef(center.GridUid, grid, indices, out var tile) || tile.Tile.IsEmpty)
                continue;

            if (pattern.StopAtObstacles && !HasClearPath(center, tile, grid))
                continue;

            if (TrySpawnFire(
                    _map.ToCenterCoordinates(tile),
                    pattern.FirePrototype,
                    pattern.Duration,
                    pattern.Intensity,
                    source,
                    out _))
            {
                count++;
            }
        }

        return count;
    }

    public IEnumerable<Vector2i> GetPatternOffsets(SurfaceFireOnTriggerComponent pattern, Vector2 direction)
    {
        var radius = Math.Max(0, pattern.Radius);
        switch (pattern.Shape)
        {
            case SurfaceFirePatternShape.Point:
                yield return Vector2i.Zero;
                yield break;

            case SurfaceFirePatternShape.Circle:
                for (var x = -radius; x <= radius; x++)
                {
                    for (var y = -radius; y <= radius; y++)
                    {
                        if (x * x + y * y <= radius * radius)
                            yield return new Vector2i(x, y);
                    }
                }

                yield break;

            case SurfaceFirePatternShape.Cone:
                var forward = direction.IsLengthZero()
                    ? Direction.East.ToIntVec()
                    : direction.GetDir().ToIntVec();
                var forwardVector = new Vector2(forward.X, forward.Y).Normalized();
                var lateralVector = new Vector2(-forwardVector.Y, forwardVector.X);
                for (var row = 0; row < radius; row++)
                {
                    var width = pattern.RowWidths != null && row < pattern.RowWidths.Count
                        ? pattern.RowWidths[row]
                        : row * 2 + 1;
                    width = Math.Max(1, width);
                    if (width % 2 == 0)
                        width++;

                    if (row == 0)
                    {
                        yield return Vector2i.Zero;
                        continue;
                    }

                    // Select the centered arc of this Chebyshev ring. This produces contiguous 1/3/5
                    // fronts for both cardinal and diagonal directions without diagonal holes.
                    var candidates = new List<(Vector2i Offset, float Lateral, float Forward)>();
                    for (var x = -row; x <= row; x++)
                    {
                        for (var y = -row; y <= row; y++)
                        {
                            if (Math.Max(Math.Abs(x), Math.Abs(y)) != row)
                                continue;

                            var offset = new Vector2(x, y);
                            var forwardDistance = Vector2.Dot(offset, forwardVector);
                            if (forwardDistance <= 0f)
                                continue;

                            candidates.Add((
                                new Vector2i(x, y),
                                Vector2.Dot(offset, lateralVector),
                                forwardDistance));
                        }
                    }

                    foreach (var candidate in candidates
                                 .OrderBy(value => Math.Abs(value.Lateral))
                                 .ThenByDescending(value => value.Forward)
                                 .Take(width)
                                 .OrderBy(value => value.Lateral))
                    {
                        yield return candidate.Offset;
                    }
                }

                yield break;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var trailQuery = EntityQueryEnumerator<SurfaceFireTrailComponent>();
        while (trailQuery.MoveNext(out var uid, out var trail))
        {
            if (!PaintTrail((uid, trail), true))
                continue;
        }

        var now = _timing.CurTime;
        var pendingFuelIgnitions = new List<PendingFuelIgnition>();
        var pendingFuelTiles = new HashSet<(EntityUid Grid, Vector2i Indices)>();
        var fireQuery = EntityQueryEnumerator<SurfaceFireComponent>();
        while (fireQuery.MoveNext(out var uid, out var fire))
        {
            var tile = Transform(uid).Coordinates.GetTileRef(EntityManager, _mapManager);
            if (tile is not { } tileRef || tileRef.Tile.IsEmpty || TileBlocksFire(tileRef))
            {
                QueueDel(uid);
                continue;
            }

            if (now < fire.NextTick)
            {
                if (now >= fire.ExpiresAt)
                    QueueDel(uid);
                continue;
            }

            fire.NextTick = now + fire.TickInterval;
            ProcessPuddle((uid, fire), tileRef, now);
            CollectNeighboringFuelIgnitions(
                (uid, fire),
                tileRef,
                now,
                pendingFuelIgnitions,
                pendingFuelTiles);
            EmitPendingVapor((uid, fire), now);

            if (now >= fire.ExpiresAt)
            {
                QueueDel(uid);
                continue;
            }

            UpdateAppearance((uid, fire), now);
            var applyDamage = now >= fire.NextDamage;
            if (applyDamage)
                fire.NextDamage = now + fire.DamageInterval;
            AffectEntities((uid, fire), applyDamage);
        }

        foreach (var ignition in pendingFuelIgnitions)
        {
            TrySpawnFire(
                ignition.Coordinates,
                ignition.Prototype,
                ignition.Duration,
                ignition.Intensity,
                ignition.Source,
                out _);
        }
    }

    public bool TrySpawnFire(
        EntityCoordinates coordinates,
        EntProtoId prototype,
        TimeSpan? duration,
        float? intensity,
        EntityUid? source,
        out EntityUid fireUid)
    {
        fireUid = EntityUid.Invalid;
        var tile = coordinates.GetTileRef(EntityManager, _mapManager);
        if (tile is not { } tileRef || tileRef.Tile.IsEmpty || !TryComp<MapGridComponent>(tileRef.GridUid, out var grid))
            return false;

        // Surface fire belongs on traversable floor. Walls and closed doors stop the flame on the previous tile.
        if (TileBlocksFire(tileRef))
            return false;

        if (TryGetFire(tileRef, out var existing))
        {
            var now = _timing.CurTime;
            var requestedDuration = duration ?? existing.Comp.Duration;
            existing.Comp.Duration = TimeSpan.FromTicks(Math.Max(existing.Comp.Duration.Ticks, requestedDuration.Ticks));
            existing.Comp.ExpiresAt = TimeSpan.FromTicks(Math.Max(existing.Comp.ExpiresAt.Ticks, (now + requestedDuration).Ticks));
            existing.Comp.Intensity = Math.Max(existing.Comp.Intensity, intensity ?? existing.Comp.Intensity);
            existing.Comp.Source = source ?? existing.Comp.Source;
            UpdateAppearance(existing, now);
            fireUid = existing.Owner;
            return true;
        }

        fireUid = Spawn(prototype, _map.ToCenterCoordinates(tileRef, grid));
        var fire = Comp<SurfaceFireComponent>(fireUid);
        var spawnTime = _timing.CurTime;
        fire.FirePrototype = prototype;
        fire.SpawnedAt = spawnTime;
        fire.Duration = duration ?? fire.Duration;
        fire.ExpiresAt = spawnTime + fire.Duration;
        fire.Intensity = intensity ?? fire.Intensity;
        fire.Source = source;
        fire.NextTick = spawnTime;
        fire.NextDamage = spawnTime;
        fire.NextFuelSpread = spawnTime + fire.FuelSpreadInterval;
        UpdateAppearance((fireUid, fire), spawnTime);
        return true;
    }

    public bool TrySpawnFire(EntityCoordinates coordinates, out EntityUid fireUid)
    {
        return TrySpawnFire(coordinates, "ForgeSurfaceFire", null, null, null, out fireUid);
    }

    public bool TryGetFire(TileRef tile, out Entity<SurfaceFireComponent> fire)
    {
        fire = default;
        if (!TryComp<MapGridComponent>(tile.GridUid, out var grid))
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var uid))
        {
            if (!TryComp<SurfaceFireComponent>(uid, out var component))
                continue;

            fire = (uid.Value, component);
            return true;
        }

        return false;
    }

    public bool ExtinguishAt(TileRef tile, FixedPoint2 reagentAmount)
    {
        if (reagentAmount <= FixedPoint2.Zero || !TryGetFire(tile, out var fire))
            return false;

        Extinguish(fire, reagentAmount.Float());
        return true;
    }

    public void Extinguish(Entity<SurfaceFireComponent> fire, float amount)
    {
        if (amount <= 0f)
            return;

        fire.Comp.ExpiresAt -= TimeSpan.FromSeconds(amount * fire.Comp.ExtinguishMultiplier);
        if (fire.Comp.ExpiresAt <= _timing.CurTime)
            QueueDel(fire);
        else
            UpdateAppearance(fire, _timing.CurTime);
    }

    public bool TileBlocksFire(TileRef tile)
    {
        if (!TryComp<MapGridComponent>(tile.GridUid, out var grid))
            return true;

        var anchored = _map.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<SurfaceFirePassThroughComponent>(uid.Value))
                continue;

            if (HasComp<BlockSurfaceFireComponent>(uid.Value) || _tags.HasTag(uid.Value, WallTag))
                return true;

            if (TryComp<DoorComponent>(uid.Value, out var door) && door.State != DoorState.Open)
                return true;
        }

        return false;
    }

    private void AffectEntities(Entity<SurfaceFireComponent> fire, bool applyDamage)
    {
        var exposure = new FireExposure(
            fire.Comp.DamagePerInterval,
            fire.Comp.FireStacksPerTick,
            fire.Comp.EffectiveIntensity,
            ApplyDamage: applyDamage);

        foreach (var target in _lookup.GetEntitiesIntersecting(fire.Owner))
        {
            if (target == fire.Owner || Deleted(target) || HasComp<SurfaceFireComponent>(target))
                continue;

            _fireExposure.ExposeEntity(target, fire.Owner, exposure, fire.Comp.Source);
        }
    }

    private void ProcessPuddle(Entity<SurfaceFireComponent> fire, TileRef tile, TimeSpan now)
    {
        if (!_puddle.TryGetPuddle(tile, out var puddleUid) ||
            !TryComp<PuddleComponent>(puddleUid, out var puddle) ||
            !_solution.ResolveSolution(puddleUid, puddle.SolutionName, ref puddle.Solution, out var puddleSolution))
        {
            if (fire.Comp.WasFuelled)
                SetFuelState(fire, 0f);
            return;
        }

        var overflowVolume = puddle.OverflowVolume;
        SetFuelState(
            fire,
            GetFuelVolume(puddleSolution, fire.Comp.Fuels),
            overflowVolume.Float());

        var changed = false;
        var addedDuration = 0d;
        var extinguishingAmount = 0f;
        foreach (var quantity in puddleSolution.Contents.ToArray())
        {
            var fuel = GetFuelConfiguration(quantity.Reagent.Prototype, fire.Comp.Fuels);
            if (fuel == null)
                continue;

            if (fuel.ConsumptionPerSecond <= 0f || fuel.DurationPerUnit <= 0f)
                continue;

            var consumptionBudget = FixedPoint2.New(
                fuel.ConsumptionPerSecond * (float) fire.Comp.TickInterval.TotalSeconds);
            if (consumptionBudget <= FixedPoint2.Zero)
                continue;

            // Preserve reagent data when carrying the consumed fuel into vapor.
            var requested = FixedPoint2.Min(quantity.Quantity, consumptionBudget);
            var removed = puddleSolution.RemoveReagent(quantity.Reagent, requested);
            if (removed <= FixedPoint2.Zero)
                continue;

            changed = true;
            addedDuration += removed.Float() * fuel.DurationPerUnit;

            if (!fuel.CarryOriginalReagents || fuel.VaporFraction <= 0f)
                continue;

            var vaporAmount = removed * Math.Clamp(fuel.VaporFraction, 0f, 1f);
            if (vaporAmount > FixedPoint2.Zero)
                fire.Comp.PendingVapor.AddReagent(quantity.Reagent, vaporAmount);
        }

        if (fire.Comp.PuddleEvaporationPerSecond > 0f)
        {
            var evaporable = puddleSolution.Contents
                .Where(quantity =>
                    GetFuelConfiguration(quantity.Reagent.Prototype, fire.Comp.Fuels) == null &&
                    _prototype.Index<ReagentPrototype>(quantity.Reagent.Prototype).Evaporates)
                .ToArray();
            var evaporableVolume = evaporable.Aggregate(
                FixedPoint2.Zero,
                (total, quantity) => total + quantity.Quantity);
            var evaporationBudget = FixedPoint2.Min(
                evaporableVolume,
                FixedPoint2.New(fire.Comp.PuddleEvaporationPerSecond * (float) fire.Comp.TickInterval.TotalSeconds));

            if (evaporationBudget > FixedPoint2.Zero && evaporableVolume > FixedPoint2.Zero)
            {
                var evaporationRatio = evaporationBudget.Float() / evaporableVolume.Float();
                foreach (var quantity in evaporable)
                {
                    var requested = FixedPoint2.Min(
                        quantity.Quantity,
                        FixedPoint2.New(quantity.Quantity.Float() * evaporationRatio));
                    var removed = puddleSolution.RemoveReagent(quantity.Reagent, requested);
                    if (removed <= FixedPoint2.Zero)
                        continue;

                    changed = true;
                    var reagent = _prototype.Index<ReagentPrototype>(quantity.Reagent.Prototype);
                    if (reagent.TileReactions.Any(reaction => reaction is ExtinguishTileReaction))
                        extinguishingAmount += removed.Float();

                    if (!fire.Comp.CarryEvaporatedReagents || fire.Comp.EvaporatedVaporFraction <= 0f)
                        continue;

                    var vaporAmount = removed * Math.Clamp(fire.Comp.EvaporatedVaporFraction, 0f, 1f);
                    if (vaporAmount > FixedPoint2.Zero)
                        fire.Comp.PendingVapor.AddReagent(quantity.Reagent, vaporAmount);
                }
            }
        }

        if (!changed)
            return;

        _solution.UpdateChemicals(puddle.Solution.Value);

        if (extinguishingAmount > 0f)
            fire.Comp.ExpiresAt -= TimeSpan.FromSeconds(extinguishingAmount * fire.Comp.ExtinguishMultiplier);

        if (addedDuration > 0d)
        {
            var extended = fire.Comp.ExpiresAt + TimeSpan.FromSeconds(addedDuration);
            var cap = now + fire.Comp.MaxFuelledDuration;
            fire.Comp.ExpiresAt = extended < cap ? extended : cap;
        }
    }

    private void CollectNeighboringFuelIgnitions(
        Entity<SurfaceFireComponent> fire,
        TileRef origin,
        TimeSpan now,
        List<PendingFuelIgnition> pending,
        HashSet<(EntityUid Grid, Vector2i Indices)> pendingTiles)
    {
        if (now < fire.Comp.NextFuelSpread ||
            fire.Comp.FuelSpreadInterval <= TimeSpan.Zero ||
            fire.Comp.MinimumFuelToSpread <= 0f ||
            !TryComp<MapGridComponent>(origin.GridUid, out var grid))
        {
            return;
        }

        fire.Comp.NextFuelSpread = now + fire.Comp.FuelSpreadInterval;
        foreach (var offset in FuelSpreadOffsets)
        {
            if (!_map.TryGetTileRef(origin.GridUid, grid, origin.GridIndices + offset, out var neighbor) ||
                neighbor.Tile.IsEmpty ||
                TileBlocksFire(neighbor) ||
                TryGetFire(neighbor, out _) ||
                !_puddle.TryGetPuddle(neighbor, out var puddleUid) ||
                !TryComp<PuddleComponent>(puddleUid, out var puddle) ||
                !_solution.ResolveSolution(puddleUid, puddle.SolutionName, ref puddle.Solution, out var solution))
            {
                continue;
            }

            var fuelVolume = GetFuelVolume(solution, fire.Comp.Fuels);
            if (fuelVolume < fire.Comp.MinimumFuelToSpread)
                continue;

            if (!pendingTiles.Add((neighbor.GridUid, neighbor.GridIndices)))
                continue;

            pending.Add(new PendingFuelIgnition(
                _map.ToCenterCoordinates(neighbor, grid),
                fire.Comp.FirePrototype,
                fire.Comp.FuelSpreadDuration,
                fire.Comp.Intensity,
                fire.Comp.Source));
        }
    }

    private float GetFuelVolume(Solution solution, List<SurfaceFireFuelConfiguration> fuels)
    {
        var total = FixedPoint2.Zero;
        foreach (var quantity in solution.Contents)
        {
            if (GetFuelConfiguration(quantity.Reagent.Prototype, fuels) != null)
                total += quantity.Quantity;
        }

        return total.Float();
    }

    private SurfaceFireFuelConfiguration? GetFuelConfiguration(
        string reagentId,
        List<SurfaceFireFuelConfiguration> fuels)
    {
        foreach (var fuel in fuels)
        {
            if (fuel.Reagent is { } reagent && reagent.Id == reagentId)
                return fuel;
        }

        foreach (var fuel in fuels)
        {
            if (fuel.Parent is not { } parent)
                continue;

            if (_prototype.EnumerateAllParents<ReagentPrototype>(reagentId)
                .Any(candidate => candidate.id == parent.Id))
            {
                return fuel;
            }
        }

        return null;
    }

    private static void SetFuelState(
        Entity<SurfaceFireComponent> fire,
        float fuelVolume,
        float? puddleOverflowVolume = null)
    {
        fire.Comp.CurrentFuelVolume = Math.Max(0f, fuelVolume);
        if (puddleOverflowVolume is { } overflowVolume)
        {
            fire.Comp.MediumFuelVolume = Math.Max(0f, overflowVolume) * SharedPuddleSystem.LowThreshold;
            fire.Comp.LargeFuelVolume = Math.Max(0f, overflowVolume) * SharedPuddleSystem.MediumThreshold;
        }

        if (fuelVolume > 0f)
            fire.Comp.WasFuelled = true;

        if (!fire.Comp.WasFuelled)
        {
            fire.Comp.FuelIntensityMultiplier = 1f;
            return;
        }

        var minimumIntensity = Math.Max(0f, fire.Comp.MinimumFuelIntensity);
        var maximumIntensity = Math.Max(minimumIntensity, fire.Comp.MaximumFuelIntensity);
        var largeVolume = Math.Max(0.01f, fire.Comp.LargeFuelVolume);
        var fraction = Math.Clamp(fire.Comp.CurrentFuelVolume / largeVolume, 0f, 1f);
        fire.Comp.FuelIntensityMultiplier = minimumIntensity +
                                            (maximumIntensity - minimumIntensity) * fraction;
    }

    private void EmitPendingVapor(Entity<SurfaceFireComponent> fire, TimeSpan now)
    {
        if (now < fire.Comp.NextVaporEmission)
            return;

        fire.Comp.NextVaporEmission = now + fire.Comp.VaporEmissionInterval;
        if (fire.Comp.PendingVapor.Volume < FixedPoint2.New(fire.Comp.MinimumVaporVolume))
            return;

        var contents = fire.Comp.PendingVapor;
        fire.Comp.PendingVapor = new Solution(Math.Max(2, contents.Contents.Count));

        _vapor.AddVapor(Transform(fire).Coordinates, fire.Comp.VaporPrototype, contents, out _);
    }

    private void UpdateAppearance(Entity<SurfaceFireComponent> fire, TimeSpan now)
    {
        if (!TryComp<AppearanceComponent>(fire, out var appearance))
            return;

        var remaining = fire.Comp.ExpiresAt - now;
        var elapsed = now - fire.Comp.SpawnedAt;
        var fraction = fire.Comp.Duration <= TimeSpan.Zero
            ? 0d
            : Math.Clamp(remaining.TotalSeconds / fire.Comp.Duration.TotalSeconds, 0d, 1d);

        SurfaceFireStage stage;
        if (elapsed < fire.Comp.InitialStageDuration)
        {
            stage = SurfaceFireStage.Initial;
        }
        else if (fire.Comp.WasFuelled)
        {
            stage = fire.Comp.CurrentFuelVolume >= fire.Comp.LargeFuelVolume
                ? SurfaceFireStage.Large
                : fire.Comp.CurrentFuelVolume >= fire.Comp.MediumFuelVolume
                    ? SurfaceFireStage.Medium
                    : SurfaceFireStage.Small;
        }
        else
        {
            stage = fraction > 0.66d
                ? SurfaceFireStage.Large
                : fraction > 0.33d
                    ? SurfaceFireStage.Medium
                    : SurfaceFireStage.Small;
        }

        _appearance.SetData(fire, SurfaceFireVisuals.Stage, stage, appearance);
    }

    private bool PaintTrail(Entity<SurfaceFireTrailComponent> trail, bool mayDeleteTrail)
    {
        if (Deleted(trail))
            return false;

        var coordinates = Transform(trail).Coordinates;
        var tile = coordinates.GetTileRef(EntityManager, _mapManager);
        if (tile is not { } current || current.Tile.IsEmpty)
            return false;

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
            return false;
        }

        if (trail.Comp.LastTile == current.GridIndices)
            return false;

        if (!TryComp<MapGridComponent>(current.GridUid, out var grid))
            return false;

        var start = trail.Comp.LastTile;
        var end = current.GridIndices;
        var x = start.X;
        var y = start.Y;
        var dx = Math.Abs(end.X - start.X);
        var sx = start.X < end.X ? 1 : -1;
        var dy = -Math.Abs(end.Y - start.Y);
        var sy = start.Y < end.Y ? 1 : -1;
        var error = dx + dy;
        var painted = false;

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
                if (TileBlocksFire(crossed))
                {
                    if (mayDeleteTrail)
                        QueueDel(trail.Owner);
                    break;
                }

                continue;
            }

            if (trail.Comp.MaxTiles > 0 && trail.Comp.PaintedTiles >= trail.Comp.MaxTiles)
            {
                if (mayDeleteTrail)
                    QueueDel(trail.Owner);
                break;
            }

            painted |= SpawnTrailTile(crossed, trail.Comp, source, mayDeleteTrail, trail.Owner);
            trail.Comp.PaintedTiles++;
            if (TileBlocksFire(crossed))
                break;
            if (trail.Comp.MaxTiles > 0 && trail.Comp.PaintedTiles >= trail.Comp.MaxTiles)
            {
                if (mayDeleteTrail)
                    QueueDel(trail.Owner);
                break;
            }
        }

        trail.Comp.LastTile = current.GridIndices;
        return painted;
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

    private bool HasClearPath(TileRef origin, TileRef target, MapGridComponent grid)
    {
        if (origin.GridUid != target.GridUid)
            return false;

        var x = origin.GridIndices.X;
        var y = origin.GridIndices.Y;
        var endX = target.GridIndices.X;
        var endY = target.GridIndices.Y;
        var dx = Math.Abs(endX - x);
        var sx = x < endX ? 1 : -1;
        var dy = -Math.Abs(endY - y);
        var sy = y < endY ? 1 : -1;
        var error = dx + dy;

        while (x != endX || y != endY)
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

            // TrySpawnFire validates the destination itself. Here only intermediate blockers matter.
            if (x == endX && y == endY)
                return true;

            if (!_map.TryGetTileRef(origin.GridUid, grid, new Vector2i(x, y), out var crossed) ||
                crossed.Tile.IsEmpty ||
                TileBlocksFire(crossed))
            {
                return false;
            }
        }

        return true;
    }

    private bool SpawnTrailTile(
        TileRef tile,
        SurfaceFireTrailComponent trail,
        EntityUid? source,
        bool mayDeleteTrail,
        EntityUid trailUid)
    {
        var spawned = TrySpawnFire(
            _map.ToCenterCoordinates(tile),
            trail.FirePrototype,
            trail.Duration,
            trail.Intensity,
            source,
            out _);

        if (mayDeleteTrail && TileBlocksFire(tile))
            QueueDel(trailUid);

        return spawned;
    }
}
