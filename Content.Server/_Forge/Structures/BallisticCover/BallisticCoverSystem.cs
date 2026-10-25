using System.Linq;
using System.Numerics;
using Content.Shared._Forge.Structures.BallisticCover;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.SegmentedEntity;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._Forge.Structures.BallisticCover;

/// <summary>
/// Adds cover behavior through physics and weapon events without replacing GunSystem or damage handling.
/// Stance, faction, and shooter facing do not affect permission to pass through cover.
/// </summary>
public sealed partial class BallisticCoverSystem : EntitySystem
{
    // A dedicated layer keeps low cover separate from visibility and other laser collisions.
    public const int CoverLayer = (int) CollisionGroup.LowCoverImpassable;

    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        InitializePenetration();
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforePhysicsSolve);
        SubscribeLocalEvent<BallisticCoverComponent, ComponentStartup>(OnCoverStartup);
        SubscribeLocalEvent<BallisticCoverComponent, ComponentShutdown>(OnCoverShutdown);
        SubscribeLocalEvent<ProjectileComponent, ComponentStartup>(OnProjectileStartup);
        SubscribeLocalEvent<BallisticCoverComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<GunComponent, TakeAmmoEvent>(OnTakeAmmo, after: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<TransformComponent, SelfBeforeGunShotEvent>(OnBeforeShot);
        SubscribeLocalEvent<TransformComponent, HitScanAfterRayCastEvent>(OnHitscan,
            after: new[] { typeof(LamiaSystem) });
        SubscribeLocalEvent<TransformComponent, ProjectileReflectAttemptEvent>(OnProjectileReflect,
            after: new[] { typeof(ReflectSystem) });
        SubscribeLocalEvent<TransformComponent, HitScanReflectAttemptEvent>(OnHitscanReflect,
            after: new[] { typeof(ReflectSystem) });
    }

    public override void Update(float frameTime)
    {
        // Another handler may cancel the shot after ammunition has been prepared.
        // In that case, AmmoShotEvent is never raised, so the context must not persist.
        _hitscanShots.Clear();
        _hitscanRays.Clear();
    }

    private void OnCoverStartup(Entity<BallisticCoverComponent> ent, ref ComponentStartup args)
    {
        ConfigureCover(ent, ent.Comp);
    }

    private void OnCoverShutdown(Entity<BallisticCoverComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent) && TryComp<FixturesComponent>(ent, out var fixtures) &&
            fixtures.Fixtures.TryGetValue(ent.Comp.Fixture, out var fixture))
        {
            _physics.RemoveCollisionLayer(ent, ent.Comp.Fixture, fixture, CoverLayer, manager: fixtures);
        }
    }

    private void ConfigureCover(EntityUid uid, BallisticCoverComponent cover)
    {
        if (TryComp<FixturesComponent>(uid, out var fixtures) &&
            fixtures.Fixtures.TryGetValue(cover.Fixture, out var fixture))
        {
            _physics.AddCollisionLayer(uid, cover.Fixture, fixture, CoverLayer, manager: fixtures);
        }
    }

    private void OnProjectileStartup(Entity<ProjectileComponent> ent, ref ComponentStartup args)
    {
        ConfigureProjectile(ent);
    }

    private void ConfigureProjectile(EntityUid uid)
    {
        if (!TryComp<FixturesComponent>(uid, out var fixtures) ||
            !fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
            return;

        _physics.SetCollisionMask(uid, SharedProjectileSystem.ProjectileFixture, fixture,
            fixture.CollisionMask | CoverLayer, manager: fixtures);
    }

    private void OnAmmoShot(Entity<GunComponent> ent, ref AmmoShotEvent args)
    {
        foreach (var uid in args.FiredProjectiles)
        {
            if (!HasComp<ProjectileComponent>(uid))
                continue;

            ConfigureProjectile(uid);
            var shot = EnsureComp<CoverProjectileComponent>(uid);
            shot.Origin = _transform.GetMoverCoordinates(uid);
            // All fired projectiles can bypass nearby cover; penetration has separate ammunition checks.
            shot.CanPass = true;
        }

        foreach (var (shooter, context) in _hitscanShots.ToArray())
        {
            if (context.Gun == ent.Owner)
                _hitscanShots.Remove(shooter);
        }
    }

    private void OnPreventCollide(Entity<BallisticCoverComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled || !TryComp<CoverProjectileComponent>(args.OtherEntity, out var shot) ||
            !TryComp<FixturesComponent>(ent, out var fixtures) ||
            !fixtures.Fixtures.TryGetValue(ent.Comp.Fixture, out var fixture) ||
            !ReferenceEquals(args.OurFixture, fixture))
            return;

        if (shot.PenetratedCovers?.Contains(ent.Owner) == true)
        {
            args.Cancelled = true;
            return;
        }

        if (!shot.CanPass ||
            CompOrNull<TargetedProjectileComponent>(args.OtherEntity)?.Target == ent.Owner ||
            !shot.Origin.IsValid(EntityManager))
            return;

        var origin = _transform.ToMapCoordinates(shot.Origin);
        var current = _transform.GetMapCoordinates(args.OtherEntity);
        if (origin.MapId != current.MapId)
            return;

        var delta = current.Position - origin.Position;
        var length = delta.Length();
        if (length < 0.001f)
        {
            // Contacts can be created before the first movement. This must not allow bypassing
            // distant cover that merely appears among broadphase contact candidates.
            var initialPose = new Robust.Shared.Physics.Transform(_transform.GetWorldPosition(ent), _transform.GetWorldRotation(ent));
            for (var child = 0; child < fixture.Shape.ChildCount; child++)
            {
                var bounds = fixture.Shape.ComputeAABB(initialPose, child);
                if (Vector2.Distance(origin.Position, bounds.ClosestPoint(origin.Position)) <= ent.Comp.PassRange)
                {
                    args.Cancelled = true;
                    break;
                }
            }
            return;
        }

        // Check the cover intersection rather than the bullet center: the bullet fixture touches cover earlier.
        // The ray must span the entire cover: a grazing intersection can be farther away
        // than the current bullet position plus one tile.
        var coverUid = ent.Owner;
        var pose = new Robust.Shared.Physics.Transform(_transform.GetWorldPosition(ent), _transform.GetWorldRotation(ent));
        var rayLength = length + 1f;
        for (var child = 0; child < fixture.Shape.ChildCount; child++)
        {
            var bounds = fixture.Shape.ComputeAABB(pose, child);
            rayLength = MathF.Max(rayLength,
                Vector2.Distance(origin.Position, bounds.Center) + bounds.Size.Length() * 0.5f);
        }
        var ray = new CollisionRay(origin.Position, delta / length, CoverLayer);
        var hits = _physics.IntersectRayWithPredicate(origin.MapId, ray, rayLength, e => e != coverUid);
        if (hits.Any())
        {
            // A distant intersection must not be overridden by the nearest point on the AABB.
            args.Cancelled = hits.Any(hit => hit.Distance <= ent.Comp.PassRange);
            return;
        }

        // The edge of a wide bullet can hit the end of cover while its center ray misses.
        // In that case, project the contact point onto the shot direction.
        for (var child = 0; child < fixture.Shape.ChildCount; child++)
        {
            var bounds = fixture.Shape.ComputeAABB(pose, child);
            var distance = Vector2.Dot(bounds.ClosestPoint(current.Position) - origin.Position, delta / length);
            if (distance <= ent.Comp.PassRange)
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    private void OnProjectileReflect(Entity<TransformComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled && TryComp<CoverProjectileComponent>(args.ProjUid, out var shot))
            shot.CanPass = false;
    }

}
