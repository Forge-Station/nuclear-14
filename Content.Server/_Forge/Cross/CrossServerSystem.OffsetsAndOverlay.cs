using System.Numerics;
using Content.Shared._Forge.Cross;
using Content.Shared.Destructible;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Map;

namespace Content.Server._Forge.Cross;

public sealed partial class CrossServerSystem
{
    private void OnCrossMapInit(Entity<CrossComponent> cross, ref MapInitEvent args)
    {
        cross.Comp.BreakInProgress = false;
        ClearAction(cross);

        if (TryGetHungTarget(cross, out var target))
        {
            PositionHungTarget(cross, target);
            RefreshMobStateVisual(target);
        }

        UpdateOccupiedOverlay(cross);
    }

    private void OnCrossMove(Entity<CrossComponent> cross, ref MoveEvent args)
    {
        if (!TryGetHungTarget(cross, out var target))
            return;

        PositionHungTarget(cross, target);
    }

    private void UpdateOccupiedOverlay(Entity<CrossComponent> cross)
    {
        if (!TryGetHungTarget(cross, out _))
        {
            RemoveOccupiedOverlay(cross.Owner);
            return;
        }

        EnsureOccupiedOverlay(cross);
    }

    private void EnsureOccupiedOverlay(Entity<CrossComponent> cross)
    {
        var crossUid = cross.Owner;

        if (_occupiedOverlays.TryGetValue(crossUid, out var existing))
        {
            if (!TerminatingOrDeleted(existing))
                return;

            _occupiedOverlays.Remove(crossUid);
        }

        var overlay = Spawn(cross.Comp.OccupiedOverlayPrototype, new EntityCoordinates(crossUid, Vector2.Zero));
        _occupiedOverlays[crossUid] = overlay;
    }

    private void RemoveOccupiedOverlay(EntityUid crossUid)
    {
        if (!_occupiedOverlays.TryGetValue(crossUid, out var overlay))
            return;

        _occupiedOverlays.Remove(crossUid);

        if (!TerminatingOrDeleted(overlay))
            QueueDel(overlay);
    }

    private void OnCrossShutdown(Entity<CrossComponent> cross, ref ComponentShutdown args)
    {
        if (TryGetHungTarget(cross, out var target))
            TryUnhangTarget(cross, target, applyBreakEffects: cross.Comp.BreakInProgress);

        PrepareCrossForRemoval(cross, breakInProgress: cross.Comp.BreakInProgress);
    }

    private void OnCrossBroken(Entity<CrossComponent> cross, ref BreakageEventArgs args)
    {
        if (TryGetHungTarget(cross, out var target))
        {
            cross.Comp.BreakInProgress = true;
            TryUnhangTarget(cross, target, applyBreakEffects: true);
        }

        PrepareCrossForRemoval(cross, breakInProgress: true);
    }

    private void OnCrossDestroyed(Entity<CrossComponent> cross, ref DestructionEventArgs args)
    {
        cross.Comp.BreakInProgress = true;

        if (TryGetHungTarget(cross, out var target))
            TryUnhangTarget(cross, target, applyBreakEffects: true);

        PrepareCrossForRemoval(cross, breakInProgress: true);
    }

    private void PrepareCrossForRemoval(Entity<CrossComponent> cross, bool breakInProgress)
    {
        cross.Comp.BreakInProgress = breakInProgress;
        ClearAction(cross);

        if (cross.Comp.HungTarget is { } hung && TryComp<HungOnCrossComponent>(hung, out var hungComp) && hungComp.Cross == cross.Owner)
            RemCompDeferred<HungOnCrossComponent>(hung);

        cross.Comp.HungTarget = null;
        RemoveOccupiedOverlay(cross.Owner);
    }

    private bool TryHangTarget(Entity<CrossComponent> cross, EntityUid target, EntityUid? user)
    {
        if (!CanHangTarget(cross, target))
            return false;

        SetHungTarget(cross, target);
        StopPullState(target, user);
        FinalizeHang(cross, target);
        return true;
    }

    private bool CanHangTarget(Entity<CrossComponent> cross, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || TerminatingOrDeleted(cross.Owner))
            return false;

        if (TryGetHungTarget(cross, out var currentTarget) && currentTarget != target)
            return false;

        if (!TryComp<HungOnCrossComponent>(target, out var existingHung))
            return true;

        return existingHung.Cross is not { } otherCross || otherCross == cross.Owner;
    }

    private void SetHungTarget(Entity<CrossComponent> cross, EntityUid target)
    {
        var hung = EnsureComp<HungOnCrossComponent>(target);
        hung.Cross = cross.Owner;
        Dirty(target, hung);

        cross.Comp.HungTarget = target;
        PositionHungTarget(cross, target);
    }

    private void StopPullState(EntityUid target, EntityUid? user)
    {
        if (TryComp<PullableComponent>(target, out var pullable))
            _pulling.TryStopPull(target, pullable, user);

        if (TryComp<PullerComponent>(target, out var puller) && puller.Pulling is { } pulled)
            _pulling.TryStopPull(pulled);
    }

    private void FinalizeHang(Entity<CrossComponent> cross, EntityUid target)
    {
        ApplyRestraints(cross, target);
        RefreshMobStateVisual(target);
        UpdateOccupiedOverlay(cross);
    }

    private bool TryUnhangTarget(Entity<CrossComponent> cross, EntityUid target, bool applyBreakEffects)
    {
        if (!CanUnhangTarget(cross, target))
            return false;

        RemoveHungTargetState(cross, target);
        PlaceUnstrappedTargetNextToCross(cross, target);
        ApplyBreakEffectsIfNeeded(cross, target, applyBreakEffects);
        FinalizeUnhang(cross, target);
        return true;
    }

    private bool CanUnhangTarget(Entity<CrossComponent> cross, EntityUid target)
    {
        return !TerminatingOrDeleted(target)
               && TryComp<HungOnCrossComponent>(target, out var hung)
               && hung.Cross == cross.Owner;
    }

    private void RemoveHungTargetState(Entity<CrossComponent> cross, EntityUid target)
    {
        RemComp<HungOnCrossComponent>(target);

        if (cross.Comp.HungTarget == target)
            cross.Comp.HungTarget = null;
    }

    private void ApplyBreakEffectsIfNeeded(Entity<CrossComponent> cross, EntityUid target, bool applyBreakEffects)
    {
        if (!applyBreakEffects)
            return;

        ApplyCrossBreakEffects(cross, target);
    }

    private void FinalizeUnhang(Entity<CrossComponent> cross, EntityUid target)
    {
        RemoveRestraints(target);
        EnsureDownedAfterUnhang(target);
        RefreshMobStateVisual(target);
        UpdateOccupiedOverlay(cross);
    }

    private bool TryGetHungTarget(Entity<CrossComponent> cross, out EntityUid target)
    {
        if (TryResolveCachedHungTarget(cross, out target))
            return true;

        cross.Comp.HungTarget = null;
        return TryFindHungTarget(cross, out target);
    }

    private bool TryResolveCachedHungTarget(Entity<CrossComponent> cross, out EntityUid target)
    {
        target = default;

        if (cross.Comp.HungTarget is not { } current || !IsHungOnCross(current, cross.Owner))
            return false;

        target = current;
        return true;
    }

    private bool TryFindHungTarget(Entity<CrossComponent> cross, out EntityUid target)
    {
        target = default;

        var children = Transform(cross.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!IsHungOnCross(child, cross.Owner))
                continue;

            cross.Comp.HungTarget = child;
            target = child;
            return true;
        }

        return false;
    }

    private bool IsHungOnCross(EntityUid target, EntityUid crossUid)
    {
        return TryComp<HungOnCrossComponent>(target, out var hung)
               && hung.Cross == crossUid
               && !TerminatingOrDeleted(target)
               && !TerminatingOrDeleted(crossUid);
    }

    private void PositionHungTarget(Entity<CrossComponent> cross, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || TerminatingOrDeleted(cross.Owner))
            return;

        var direction = _transform.GetWorldRotation(cross.Owner).GetCardinalDir();
        var offset = cross.Comp.GetBuckleOffset(direction, Vector2.Zero);
        var coords = new EntityCoordinates(cross.Owner, offset);
        var xform = Transform(target);
        _transform.SetCoordinates(target, xform, coords, rotation: Angle.Zero);
    }

    private void PlaceUnstrappedTargetNextToCross(Entity<CrossComponent> cross, EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return;

        _transform.AttachToGridOrMap(target);

        if (TerminatingOrDeleted(cross.Owner))
            return;

        var direction = _transform.GetWorldRotation(cross.Owner).GetCardinalDir();
        var offset = GetUnstrapOffset(direction, cross.Comp.UnstrapDistance);
        var crossCoords = _transform.GetMapCoordinates(cross.Owner);
        var targetMapCoords = new MapCoordinates(crossCoords.Position + offset, crossCoords.MapId);
        _transform.SetMapCoordinates(target, targetMapCoords);
    }

    private static Vector2 GetUnstrapOffset(Direction direction, float distance)
    {
        return direction switch
        {
            Direction.North => new Vector2(0f, -distance),
            Direction.South => new Vector2(0f, distance),
            Direction.East => new Vector2(-distance, 0f),
            Direction.West => new Vector2(distance, 0f),
            _ => new Vector2(0f, -distance)
        };
    }

    private void ApplyCrossBreakEffects(Entity<CrossComponent> cross, EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return;

        _damageable.TryChangeDamage(target, cross.Comp.BreakDamage, origin: cross.Owner);
        _stun.TryParalyze(target, cross.Comp.BreakStunDuration, refresh: true);
    }
}

