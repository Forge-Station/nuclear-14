using System.Linq;
using Content.Shared._Forge.Cross;
using Content.Shared.Cuffs.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Rotation;
using Content.Shared.Standing;

namespace Content.Server._Forge.Cross;

public sealed partial class CrossServerSystem
{
    private void OnUncuffAttempt(ref UncuffAttemptEvent args)
    {
        if (args.Cancelled || !IsHungOnCrossWithCrossRestraints(args.Target))
            return;

        args.Cancelled = true;

        if (!Exists(args.User))
            return;

        if (!TryComp<HungOnCrossComponent>(args.Target, out var hung) ||
            hung.Cross is not { } crossUid ||
            !TryComp<CrossComponent>(crossUid, out var crossComp))
        {
            _popup.PopupClient(Loc.GetString("n14-cross-popup-cant-uncuff-while-hung"), args.User, args.User);
            return;
        }

        TryStartUnhangAction((crossUid, crossComp), args.User, args.Target, popup: true);
    }

    private void OnMobStateChanged(EntityUid uid, HungOnCrossComponent component, MobStateChangedEvent args)
    {
        RefreshMobStateVisual(uid, args.Component);
    }

    private void OnDowned(EntityUid uid, HungOnCrossComponent component, ref DownedEvent args)
    {
        _appearance.SetData(uid, RotationVisuals.RotationState, RotationState.Vertical);
    }

    private bool IsHungOnCrossWithCrossRestraints(EntityUid target)
    {
        return IsHungOnCross(target) && HasCrossRestraints(target);
    }

    private void ApplyRestraints(Entity<CrossComponent> cross, EntityUid target)
    {
        if (!TryComp<HandsComponent>(target, out _))
            return;

        var cuffable = EnsureComp<CuffableComponent>(target);
        if (HasCrossRestraints((target, cuffable)))
            return;

        var restraints = Spawn(cross.Comp.RestraintPrototype, Transform(target).Coordinates);
        if (!TryComp<HandcuffComponent>(restraints, out _) || !_cuffs.TryAddNewCuffs(target, target, restraints, cuffable))
            QueueDel(restraints);
    }

    private void RemoveRestraints(EntityUid target)
    {
        if (!TryComp<CuffableComponent>(target, out var cuffable))
            return;

        foreach (var cuffs in _cuffs.GetAllCuffs(cuffable).ToArray())
        {
            if (!HasComp<CrossRestraintComponent>(cuffs))
                continue;

            var cuffsBefore = cuffable.CuffedHandCount;
            _cuffs.Uncuff(target, null, cuffs, cuffable);

            // If Uncuff ignored these cuffs, remove them directly from the cuff container.
            if (cuffable.CuffedHandCount == cuffsBefore)
            {
                _container.Remove(cuffs, cuffable.Container, force: true);
                QueueDel(cuffs);
            }
        }
    }

    private void EnsureDownedAfterUnhang(EntityUid target, MobStateComponent? mobState = null)
    {
        if (!Resolve(target, ref mobState, false))
            return;

        if (mobState.CurrentState is not (MobState.Dead or MobState.Critical or MobState.SoftCritical))
            return;

        _standing.Down(target, playSound: false, dropHeldItems: false);
    }

    private void RefreshMobStateVisual(EntityUid target, MobStateComponent? mobState = null)
    {
        if (!Resolve(target, ref mobState, false))
            return;

        var visualState = mobState.CurrentState;
        if (IsHungOnCross(target) && visualState is MobState.Critical or MobState.SoftCritical)
            visualState = MobState.Alive;

        _appearance.SetData(target, MobStateVisuals.State, visualState);

        if (IsHungOnCross(target))
        {
            _appearance.SetData(target, RotationVisuals.RotationState, RotationState.Vertical);
            return;
        }

        if (TryComp<StandingStateComponent>(target, out var standing))
        {
            var rotationState = standing.CurrentState == StandingState.Standing
                ? RotationState.Vertical
                : RotationState.Horizontal;

            _appearance.SetData(target, RotationVisuals.RotationState, rotationState);
        }
    }

    private bool IsHungOnCross(EntityUid target)
    {
        return TryComp<HungOnCrossComponent>(target, out var hung)
               && hung.Cross is { } cross
               && HasComp<CrossComponent>(cross);
    }

    private bool HasCrossRestraints(EntityUid target)
    {
        return TryComp<CuffableComponent>(target, out var cuffable)
               && HasCrossRestraints((target, cuffable));
    }

    private bool HasCrossRestraints(Entity<CuffableComponent> ent)
    {
        return _cuffs.GetAllCuffs(ent.Comp).Any(cuffs => HasComp<CrossRestraintComponent>(cuffs));
    }
}

