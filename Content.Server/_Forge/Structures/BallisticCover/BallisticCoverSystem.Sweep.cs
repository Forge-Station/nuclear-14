using System.Numerics;
using Content.Shared._Forge.Structures.BallisticCover;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Forge.Structures.BallisticCover;

public sealed partial class BallisticCoverSystem
{
    [Dependency] private readonly RayCastSystem _casts = default!;
    [Dependency] private readonly EntityLookupSystem _coverLookup = default!;
    private readonly HashSet<EntityUid> _coverCandidates = new();

    private void OnBeforePhysicsSolve(ref PhysicsUpdateBeforeSolveEvent args)
    {
        if (Count<BallisticCoverComponent>() == 0)
            return;
        var query = EntityQueryEnumerator<CoverProjectileComponent, ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var projectile, out var body, out var xform))
        {
            if (projectile.DamagedEntity || !body.CanCollide || !body.Awake || TerminatingOrDeleted(uid) ||
                !TryComp<FixturesComponent>(uid, out var fixtures) ||
                !fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
                continue;

            var translation = _physics.GetMapLinearVelocity(uid, body, xform) * args.DeltaTime;
            var length = translation.Length();
            if (length < 0.001f)
                continue;

            var start = _transform.GetMapCoordinates(uid, xform);
            var pose = new Robust.Shared.Physics.Transform(start.Position, _transform.GetWorldRotation(uid));
            var endPose = new Robust.Shared.Physics.Transform(start.Position + translation, pose.Quaternion2D.Angle);
            var sweptBounds = fixture.Shape.ComputeAABB(pose, 0).Union(fixture.Shape.ComputeAABB(endPose, 0));
            if (!MayCrossCover(start.MapId, sweptBounds))
                continue;
            // Keep callback creation in a separate method; otherwise the compiler allocates
            // closures before the preliminary checks in this loop.
            SweepNearCover((uid, projectile, body, xform), fixtures, fixture, translation, start, pose);
        }
    }

    private void SweepNearCover(Entity<ProjectileComponent, PhysicsComponent, TransformComponent> ent,
        FixturesComponent fixtures, Fixture fixture, Vector2 translation, MapCoordinates start,
        Robust.Shared.Physics.Transform pose)
    {
        var uid = ent.Owner;
        var projectile = ent.Comp1;
        var body = ent.Comp2;
        var xform = ent.Comp3;
        var length = translation.Length();
        var blockingCover = false;
        var fraction = 1f;
        var filter = new QueryFilter
        {
            LayerBits = fixture.CollisionLayer,
            MaskBits = fixture.CollisionMask,
            IsIgnored = other => other == uid || projectile.IgnoreShooter &&
                (other == projectile.Shooter || other == projectile.Weapon),
        };

        // The soft bullet fixture is checked discretely and can cross thin cover in one tick.
        // Sweep the entire shape over the upcoming substep.
        // The nearest wall or ordinary target retains priority over cover.
        float ConsiderHit(EntityUid other, PhysicsComponent otherBody, Fixture otherFixture, float hitFraction)
        {
            if (hitFraction > fraction)
                return fraction;
            var prevent = new PreventCollideEvent(other, uid, otherBody, body, otherFixture, fixture);
            RaiseLocalEvent(other, ref prevent);
            if (prevent.Cancelled)
                return -1f;
            prevent = new PreventCollideEvent(uid, other, body, otherBody, fixture, otherFixture);
            RaiseLocalEvent(uid, ref prevent);
            if (prevent.Cancelled)
                return -1f;
            fraction = hitFraction;
            blockingCover = HasComp<BallisticCoverComponent>(other);
            return fraction;
        }

        _casts.CastShape(start.MapId, fixture.Shape, pose, translation, filter,
            (FixtureProxy proxy, Vector2 point, Vector2 normal, float hitFraction, ref RayResult result) =>
                ConsiderHit(proxy.Entity, proxy.Body, proxy.Fixture, hitFraction));

        // The ray broadphase only selects candidates; check their actual shapes
        // independently of GJK, which misses some rectangular shapes.
        var rayCandidates = _physics.IntersectRayWithPredicate(start.MapId,
            new CollisionRay(start.Position, translation / length, fixture.CollisionMask), length,
            filter.IsIgnored, returnOnFirstHit: false);
        foreach (var hit in rayCandidates)
        {
            if (!TryComp<FixturesComponent>(hit.HitEntity, out var hitFixtures) ||
                !TryComp<PhysicsComponent>(hit.HitEntity, out var hitBody))
                continue;
            foreach (var hitFixture in hitFixtures.Fixtures.Values)
            {
                if (hitFixture.Hard && (hitFixture.CollisionLayer & fixture.CollisionMask) != 0 &&
                    TryFixtureRayHit(hit.HitEntity, hitFixture, start.Position, translation, out var hitFraction))
                    ConsiderHit(hit.HitEntity, hitBody, hitFixture, hitFraction);
            }
        }

        if (!blockingCover || HasBlockingOverlap(uid, body, xform, fixtures, fixture, pose, filter))
            return;

        // Advance the bullet to contact before the engine's CollideContacts. ProjectileSystem
        // then handles reflection, damage, effects, and projectile deletion.
        // A small inward offset compensates for GJK tolerance when finding the first contact.
        var distance = MathF.Min(length * fraction + 0.02f, length);
        _transform.SetMapCoordinates(uid, new MapCoordinates(start.Position + translation / length * distance, start.MapId));
    }

    private bool MayCrossCover(MapId mapId, Box2 bounds)
    {
        // Use a cheap query against the existing spatial tree. If no cover is on the step
        // path, do not create a ShapeCast or check the geometry of other targets.
        _coverCandidates.Clear();
        _coverLookup.GetEntitiesIntersecting(mapId, bounds, _coverCandidates,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Approximate);
        foreach (var candidate in _coverCandidates)
        {
            if (HasComp<BallisticCoverComponent>(candidate))
                return true;
        }
        return false;
    }

    private bool HasBlockingOverlap(EntityUid uid, PhysicsComponent body, TransformComponent xform,
        FixturesComponent fixtures, Fixture fixture, Robust.Shared.Physics.Transform pose, QueryFilter filter)
    {
        // ShapeCast does not report initial overlaps. Before advancing to cover, leave
        // a wall or other target already overlapping the bullet to the engine physics.
        var bounds = fixture.Shape.ComputeAABB(pose, 0);
        foreach (var other in _physics.GetEntitiesIntersectingBody(uid, fixture.CollisionMask, false, body, fixtures, xform))
        {
            if (filter.IsIgnored?.Invoke(other) == true || !TryComp<FixturesComponent>(other, out var otherFixtures) ||
                !TryComp<PhysicsComponent>(other, out var otherBody))
                continue;
            var otherPose = new Robust.Shared.Physics.Transform(_transform.GetWorldPosition(other), _transform.GetWorldRotation(other));
            foreach (var otherFixture in otherFixtures.Fixtures.Values)
            {
                if (!otherFixture.Hard || (otherFixture.CollisionLayer & fixture.CollisionMask) == 0)
                    continue;
                for (var child = 0; child < otherFixture.Shape.ChildCount; child++)
                {
                    if (!bounds.Intersects(otherFixture.Shape.ComputeAABB(otherPose, child)) ||
                        !FixturesOverlap(uid, fixture, body, pose, other, otherFixture, otherBody, otherPose))
                        continue;
                    var prevent = new PreventCollideEvent(other, uid, otherBody, body, otherFixture, fixture);
                    RaiseLocalEvent(other, ref prevent);
                    if (prevent.Cancelled)
                        continue;
                    prevent = new PreventCollideEvent(uid, other, body, otherBody, fixture, otherFixture);
                    RaiseLocalEvent(uid, ref prevent);
                    if (!prevent.Cancelled)
                        return true;
                }
            }
        }
        return false;
    }
}
