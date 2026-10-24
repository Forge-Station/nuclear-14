using Content.Shared._Forge.Structures.BallisticCover;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Physics;

namespace Content.Server._Forge.Structures.BallisticCover;
// Handles hitscan shots and their context.
public sealed partial class BallisticCoverSystem
{
    // Hitscan runs synchronously inside Shoot; its context lasts until AmmoShotEvent.
    private readonly Dictionary<EntityUid, HitscanContext> _hitscanShots = new();
    private readonly Dictionary<EntityUid, List<RayCastResults>> _hitscanRays = new();

    private void OnTakeAmmo(Entity<GunComponent> ent, ref TakeAmmoEvent args)
    {
        PrepareHitscan(args.User ?? ent.Owner, ent, args.Ammo);
    }

    private void OnBeforeShot(Entity<TransformComponent> ent, ref SelfBeforeGunShotEvent args)
    {
        if (!args.Cancelled && ent.Owner == args.Shooter)
            PrepareHitscan(args.Shooter, args.Gun, args.Ammo);
    }

    private void PrepareHitscan(EntityUid shooter, Entity<GunComponent> gun,
        List<(EntityUid? Entity, IShootable Shootable)> ammo)
    {
        var hasHitscan = false;
        for (var i = 0; i < ammo.Count; i++)
        {
            var (entity, shootable) = ammo[i];
            if (shootable is not HitscanPrototype hitscan)
                continue;

            hasHitscan = true;
            if ((hitscan.CollisionMask & CoverLayer) != 0)
                continue;

            // The prototype is global: expand the mask only on a separate copy for this shot.
            var copy = _serialization.CreateCopy(hitscan, notNullableOverride: true);
            copy.CollisionMask |= CoverLayer;
            ammo[i] = (entity, copy);
        }

        if (hasHitscan)
            _hitscanShots[shooter] = new HitscanContext(gun, gun.Comp.Target);
    }

    internal void CaptureHitscanRay(EntityUid uid, List<RayCastResults>? hits)
    {
        if (hits != null)
            _hitscanRays[uid] = hits;
    }

    private void OnHitscan(Entity<TransformComponent> ent, ref HitScanAfterRayCastEvent args)
    {
        _hitscanRays.Remove(ent.Owner, out var original);
        if (args.RayCastResults is not { } hits)
            return;

        if (_hitscanShots.TryGetValue(ent.Owner, out var context) && !context.Reflected)
        {
            var passedShapes = new HashSet<(EntityUid, int)>();
            hits.RemoveAll(hit => CanPassHitscanHit(hit, context.Target, passedShapes));
        }

        // LamiaSystem replaces the list, but GunSystem later reads the original list again.
        // Preserve filtering from both systems in the same collection that GunSystem received.
        if (original != null && !ReferenceEquals(original, hits))
        {
            original.Clear();
            original.AddRange(hits);
            args.RayCastResults = original;
        }

        // Having no target is valid, including after excluding the shooter's own segments.
        if (hits.Count == 0)
            args.RayCastResults = null;
    }

    private bool CanPassHitscanHit(RayCastResults hit, EntityUid? target, HashSet<(EntityUid, int)> passedShapes)
    {
        if (hit.HitEntity == target || !TryComp<BallisticCoverComponent>(hit.HitEntity, out var cover) ||
            hit.Distance > cover.PassRange || !TryComp<FixturesComponent>(hit.HitEntity, out var fixtures) ||
            !fixtures.Fixtures.TryGetValue(cover.Fixture, out var fixture) || !fixture.Hard)
            return false;

        var pose = new Robust.Shared.Physics.Transform(_transform.GetWorldPosition(hit.HitEntity),
            _transform.GetWorldRotation(hit.HitEntity));
        for (var child = 0; child < fixture.Shape.ChildCount; child++)
        {
            // RayCastResults does not identify the fixture. Skip only one match per child
            // of the low fixture; a matching solid fixture must still block the shot.
            if (passedShapes.Contains((hit.HitEntity, child)) ||
                !fixture.Shape.ComputeAABB(pose, child).Enlarged(0.0001f).Contains(hit.HitPos))
                continue;

            passedShapes.Add((hit.HitEntity, child));
            return true;
        }

        return false;
    }

    private void OnHitscanReflect(Entity<TransformComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (!args.Reflected)
            return;

        foreach (var context in _hitscanShots.Values)
        {
            if (context.Gun == args.SourceItem)
                context.Reflected = true;
        }
    }

    private sealed class HitscanContext(EntityUid gun, EntityUid? target)
    {
        public readonly EntityUid Gun = gun;
        public readonly EntityUid? Target = target;
        public bool Reflected;
    }
}
