using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._Forge.Fire;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Smoking;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Fire;

/// <summary>
/// Owns persistent, mass-bounded chemical vapor clouds created by heated puddles.
/// </summary>
public sealed class SurfaceVaporSystem : EntitySystem
{
    private static readonly Vector2i[] CardinalOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    private static readonly Vector2i[] DiagonalOffsets =
    {
        new(1, 1),
        new(1, -1),
        new(-1, 1),
        new(-1, -1),
    };

    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly OccluderSystem _occluder = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SmokeSystem _smoke = default!;
    [Dependency] private readonly SolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurfaceVaporComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<SurfaceVaporComponent> ent, ref MapInitEvent args)
    {
        var now = _timing.CurTime;
        ent.Comp.NextUpdate = now + ent.Comp.UpdateInterval;
        ent.Comp.LastSupplied = now;
        UpdateState(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SurfaceVaporComponent, SmokeComponent>();
        while (query.MoveNext(out var uid, out var vapor, out var smoke))
        {
            if (now < vapor.NextUpdate)
                continue;

            var elapsed = Math.Max(0f, (float) (now - vapor.NextUpdate + vapor.UpdateInterval).TotalSeconds);
            vapor.NextUpdate = now + vapor.UpdateInterval;

            if (!_solution.ResolveSolution(uid, SmokeComponent.SolutionName, ref smoke.Solution, out var solution))
            {
                QueueDel(uid);
                continue;
            }

            var decay = FixedPoint2.New(Math.Max(0f, vapor.DecayPerSecond) * elapsed);
            if (decay > FixedPoint2.Zero && solution.Volume > FixedPoint2.Zero)
            {
                solution.SplitSolution(FixedPoint2.Min(decay, solution.Volume));
                _solution.UpdateChemicals(smoke.Solution.Value);
            }

            if (solution.Volume <= vapor.DeleteThreshold && now - vapor.LastSupplied >= vapor.SupplyGracePeriod)
            {
                QueueDel(uid);
                continue;
            }

            smoke.TransferRate = solution.Volume / Math.Max(0.1f, vapor.ExposureDuration);
            Dirty(uid, smoke);
            UpdateState((uid, vapor));
        }
    }

    /// <summary>
    /// Adds reagent mass to the single vapor entity occupying a tile. Excess over MaxVolume is treated as
    /// dilution into the unrepresented room volume and is deliberately discarded rather than spread or cloned.
    /// </summary>
    public bool AddVapor(EntityCoordinates coordinates, EntProtoId prototype, Solution contents, out EntityUid vaporUid)
    {
        vaporUid = EntityUid.Invalid;
        if (contents.Volume <= FixedPoint2.Zero)
            return false;

        var tile = coordinates.GetTileRef(EntityManager, _mapManager);
        if (tile is not { } tileRef || tileRef.Tile.IsEmpty)
            return false;

        Entity<SurfaceVaporComponent, SmokeComponent> vapor;
        if (!TryGetVapor(tileRef, out vapor))
        {
            vaporUid = Spawn(prototype, _map.ToCenterCoordinates(tileRef));
            vapor = (vaporUid, Comp<SurfaceVaporComponent>(vaporUid), Comp<SmokeComponent>(vaporUid));
        }
        else
        {
            vaporUid = vapor.Owner;
        }

        if (!_solution.ResolveSolution(vapor.Owner, SmokeComponent.SolutionName, ref vapor.Comp2.Solution, out var solution))
            return false;

        var available = FixedPoint2.Max(FixedPoint2.Zero, vapor.Comp1.MaxVolume - solution.Volume);
        var acceptedVolume = FixedPoint2.Min(available, contents.Volume);
        if (acceptedVolume <= FixedPoint2.Zero)
        {
            vapor.Comp1.LastSupplied = _timing.CurTime;
            return true;
        }

        var accepted = contents.SplitSolution(acceptedVolume);
        _smoke.StartSmoke(
            vapor.Owner,
            accepted,
            Math.Max(0.1f, vapor.Comp1.ExposureDuration),
            0,
            vapor.Comp2,
            reactOnTile: false,
            timedDespawn: false,
            activateSpread: false);

        vapor.Comp1.LastSupplied = _timing.CurTime;
        if (_solution.ResolveSolution(vapor.Owner, SmokeComponent.SolutionName, ref vapor.Comp2.Solution, out solution))
        {
            vapor.Comp2.TransferRate = solution.Volume / Math.Max(0.1f, vapor.Comp1.ExposureDuration);
            Dirty(vapor.Owner, vapor.Comp2);
        }

        UpdateState((vapor.Owner, vapor.Comp1));
        return true;
    }

    public bool TryGetVapor(TileRef tile, out Entity<SurfaceVaporComponent, SmokeComponent> vapor)
    {
        vapor = default;
        if (!TryComp<MapGridComponent>(tile.GridUid, out var grid))
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var uid))
        {
            if (!TryComp<SurfaceVaporComponent>(uid.Value, out var vaporComp) ||
                !TryComp<SmokeComponent>(uid.Value, out var smokeComp))
            {
                continue;
            }

            vapor = (uid.Value, vaporComp, smokeComp);
            return true;
        }

        return false;
    }

    private void UpdateState(Entity<SurfaceVaporComponent> vapor)
    {
        if (!TryComp<SmokeComponent>(vapor, out var smoke) ||
            !_solution.ResolveSolution(vapor.Owner, SmokeComponent.SolutionName, ref smoke.Solution, out var solution))
        {
            return;
        }

        vapor.Comp.EffectiveDensity = CalculateEffectiveDensity(vapor, solution.Volume.Float());
        vapor.Comp.Dense = vapor.Comp.Dense
            ? vapor.Comp.EffectiveDensity >= vapor.Comp.DenseExitThreshold
            : vapor.Comp.EffectiveDensity >= vapor.Comp.DenseEnterThreshold;

        var stage = vapor.Comp.Dense
            ? SurfaceVaporStage.Dense
            : vapor.Comp.EffectiveDensity >= vapor.Comp.NormalThreshold
                ? SurfaceVaporStage.Normal
                : SurfaceVaporStage.Weak;

        _appearance.SetData(vapor.Owner, SmokeVisuals.Color, solution.GetColor(_prototype));
        _appearance.SetData(vapor.Owner, SurfaceVaporVisuals.Stage, stage);

        if (TryComp<OccluderComponent>(vapor, out var occluder))
            _occluder.SetEnabled(vapor.Owner, vapor.Comp.Dense, occluder);
    }

    private float CalculateEffectiveDensity(Entity<SurfaceVaporComponent> vapor, float localVolume)
    {
        var tile = Transform(vapor).Coordinates.GetTileRef(EntityManager, _mapManager);
        if (tile is not { } tileRef || !TryComp<MapGridComponent>(tileRef.GridUid, out var grid))
            return localVolume;

        var density = localVolume;
        foreach (var offset in CardinalOffsets)
            density += GetNeighborVolume(tileRef, grid, offset) * Math.Max(0f, vapor.Comp.CardinalNeighborWeight);
        foreach (var offset in DiagonalOffsets)
            density += GetNeighborVolume(tileRef, grid, offset) * Math.Max(0f, vapor.Comp.DiagonalNeighborWeight);
        return density;
    }

    private float GetNeighborVolume(TileRef origin, MapGridComponent grid, Vector2i offset)
    {
        if (!_map.TryGetTileRef(origin.GridUid, grid, origin.GridIndices + offset, out var tile) ||
            !TryGetVapor(tile, out var neighbor) ||
            !_solution.ResolveSolution(neighbor.Owner, SmokeComponent.SolutionName, ref neighbor.Comp2.Solution, out var solution))
        {
            return 0f;
        }

        return solution.Volume.Float();
    }
}
