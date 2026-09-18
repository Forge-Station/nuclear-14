using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._Nuclear14.Bosses;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Nuclear14.Bosses;

public sealed class MilitarySentrybotSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _npc = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly RotateToFaceSystem _rotate = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _hostiles = new();
    private readonly HashSet<Entity<NpcFactionMemberComponent>> _nearbyNpcs = new();

    private const double RotateSpeed = 1.5;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MilitarySentrybotComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<MilitarySentrybotComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.AimIndicatorUid != null && Exists(ent.Comp.AimIndicatorUid.Value))
            QueueDel(ent.Comp.AimIndicatorUid.Value);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<MilitarySentrybotComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            var now = _timing.CurTime;

            if (!RefreshTarget(uid, comp, xform))
            {
                ResetFocus(comp);
                continue;
            }

            var target = comp.Target!.Value;

            if (_mobState.IsIncapacitated(target))
            {
                comp.Target = null;
                ResetFocus(comp);
                continue;
            }

            var worldPos = _transform.GetMapCoordinates(uid, xform);
            var targetPos = _transform.GetMapCoordinates(target);

            // Only focus and fire the rocket at targets within the focus range;
            // further ones are still chased and lasered by the HTN.
            if ((targetPos.Position - worldPos.Position).Length() > comp.FocusRange)
            {
                // If the current target left the focus range, switch to the nearest target that is within it.
                CollectHostiles(uid, comp, xform);
                if (PickNearestTarget(uid, comp, xform, comp.FocusRange) is {} closer)
                    comp.Target = closer;

                ResetFocus(comp);
                continue;
            }

            // Face the rocket-focus target unless the HTN is already tracking something with the lasers.
            if (!HasLaserTarget(uid))
                _rotate.TryRotateTo(uid, Angle.FromWorldVec(targetPos.Position - worldPos.Position), frameTime, Angle.FromDegrees(1), RotateSpeed, xform);

            if (now < comp.NextAttack)
                continue;

            comp.LosCheckTimer -= frameTime;

            if (comp.LosCheckTimer <= 0)
            {
                comp.LosCheckTimer = 0.2f;

                if (!CanSee(target, comp, worldPos))
                {
                    // The current target is out of sight - switch to the nearest target that is both
                    // within focus range and actually visible to the rockets.
                    CollectHostiles(uid, comp, xform);
                    if (PickNearestTarget(uid, comp, xform, comp.FocusRange, requireVisible: true, startPos: worldPos) is {} closer)
                        comp.Target = closer;

                    ResetFocus(comp);
                    continue;
                }
            }

            if (comp.AimIndicatorUid == null || !Exists(comp.AimIndicatorUid.Value))
                SpawnIndicator(comp, target);

            comp.FocusAccumulator += frameTime;

            if (comp.FocusAccumulator >= comp.FocusTime)
            {
                var rocket = Spawn(comp.Rocket, worldPos);
                var dir = targetPos.Position - worldPos.Position;

                dir = dir.LengthSquared() > 0.001f ? dir.Normalized() : Vector2.UnitX;

                _gun.ShootProjectile(rocket, dir, Vector2.Zero, uid, uid, comp.RocketSpeed);
                DeleteIndicator(comp);
                comp.FocusAccumulator = 0f;
                comp.NextAttack = now + TimeSpan.FromSeconds(comp.AttackCooldown);
                comp.LosCheckTimer = 0f;
            }
        }
    }

    private bool RefreshTarget(EntityUid uid, MilitarySentrybotComponent comp, TransformComponent xform)
    {
        if (comp.Target != null && (!Exists(comp.Target.Value) || Terminating(comp.Target.Value)))
            comp.Target = null;

        if (comp.Target == null)
        {
            CollectHostiles(uid, comp, xform);
            comp.Target = PickNearestTarget(uid, comp, xform);

            if (comp.Target == null)
                return false;
        }

        return true;
    }

    private void CollectHostiles(EntityUid uid, MilitarySentrybotComponent comp, TransformComponent xform)
    {
        _hostiles.Clear();
        _npc.GetNearbyHostiles(uid, comp.VisionRange, _hostiles);

        var worldPos = _transform.GetMapCoordinates(uid, xform);

        // Also treat any nearby non-allied NPC (animals, insects, ferals, raiders, etc.) as a target,
        // regardless of whether our faction lists theirs.
        _nearbyNpcs.Clear();
        _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(worldPos, comp.VisionRange, _nearbyNpcs);

        foreach (var candidate in _nearbyNpcs)
        {
            if (candidate.Owner == uid || _hostiles.Contains(candidate.Owner))
                continue;

            if (_mobState.IsIncapacitated(candidate.Owner))
                continue;

            if (!TryComp(uid, out NpcFactionMemberComponent? ourFaction))
                continue;

            if (!_npc.IsEntityFriendly((uid, ourFaction), (candidate.Owner, candidate.Comp)))
                _hostiles.Add(candidate.Owner);
        }
    }

    private EntityUid? PickNearestTarget(EntityUid uid, MilitarySentrybotComponent comp, TransformComponent xform,
        float? maxRange = null, bool requireVisible = false, MapCoordinates? startPos = null)
    {
        var worldPos = startPos ?? _transform.GetMapCoordinates(uid, xform);
        var maxRangeSq = maxRange is {} range ? range * range : float.MaxValue;

        EntityUid? best = null;
        var bestDist = float.MaxValue;

        foreach (var hostile in _hostiles)
        {
            if (!TryComp(hostile, out TransformComponent? hxform) || hxform.MapUid != xform.MapUid)
                continue;

            if (_mobState.IsIncapacitated(hostile))
                continue;

            var diff = _transform.GetMapCoordinates(hostile, xform: hxform).Position - worldPos.Position;
            var distSq = diff.LengthSquared();

            if (distSq > maxRangeSq)
                continue;

            if (requireVisible && !CanSee(hostile, comp, worldPos))
                continue;

            if (distSq < bestDist)
            {
                bestDist = distSq;
                best = hostile;
            }
        }

        return best;
    }

    private void ResetFocus(MilitarySentrybotComponent comp)
    {
        comp.FocusAccumulator = 0f;
        comp.LosCheckTimer = 0f;
        DeleteIndicator(comp);
    }

    private bool HasLaserTarget(EntityUid uid)
    {
        return TryComp(uid, out HTNComponent? htn)
            && htn.Blackboard.TryGetValue<EntityUid>("Target", out _, EntityManager);
    }

    private void SpawnIndicator(MilitarySentrybotComponent comp, EntityUid target)
    {
        var targetPos = _transform.GetMapCoordinates(target);
        var indicator = Spawn(comp.AimIndicator, targetPos);

        _transform.SetParent(indicator, target);

        if (TryComp(indicator, out TransformComponent? ixform))
            _transform.SetLocalPosition(indicator, new Vector2(0f, -0.55f), ixform);

        if (TryComp(indicator, out MilitaryAimIndicatorComponent? aimComp))
        {
            aimComp.Duration = comp.FocusTime;
            Dirty(indicator, aimComp);
        }

        comp.AimIndicatorUid = indicator;
    }

    private void DeleteIndicator(MilitarySentrybotComponent comp)
    {
        if (comp.AimIndicatorUid != null && Exists(comp.AimIndicatorUid.Value))
            QueueDel(comp.AimIndicatorUid.Value);

        comp.AimIndicatorUid = null;
    }

    private bool CanSee(EntityUid target, MilitarySentrybotComponent comp, MapCoordinates worldPos)
    {
        if (!TryComp(target, out TransformComponent? txform) || txform.MapUid == null)
            return false;

        var targetPos = _transform.GetMapCoordinates(target, xform: txform);
        return _interaction.InRangeUnobstructed(worldPos, targetPos, comp.VisionRange);
    }
}