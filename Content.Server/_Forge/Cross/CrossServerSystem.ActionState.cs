using Content.Shared._Forge.Cross;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;

namespace Content.Server._Forge.Cross;

public sealed partial class CrossServerSystem
{
    private bool TryStartHangDoAfter(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        var ev = new CrossHangDoAfterEvent
        {
            HangTarget = GetNetEntity(target),
            ActionId = cross.Comp.ActionId,
        };

        return TryStartCrossDoAfter(cross, user, cross.Comp.HangDelay, ev);
    }

    private bool TryStartUnhangDoAfter(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        var ev = new CrossUnhangDoAfterEvent
        {
            UnhangTarget = GetNetEntity(target),
            ActionId = cross.Comp.ActionId,
        };

        return TryStartCrossDoAfter(cross, user, cross.Comp.UnhangDelay, ev);
    }

    private bool HandleDoAfterInterruption(
        Entity<CrossComponent> cross,
        bool handled,
        bool cancelled,
        EntityUid user,
        string interruptionLoc)
    {
        if (!handled && !cancelled)
            return false;

        if (cancelled && Exists(user))
            _popup.PopupEntity(Loc.GetString(interruptionLoc), cross.Owner, user);

        return true;
    }

    private bool AreDoAfterParticipantsInvalid(EntityUid user, EntityUid target)
    {
        return !Exists(user) || !Exists(target) || TerminatingOrDeleted(target);
    }

    private bool TryBeginAction(
        Entity<CrossComponent> cross,
        CrossActionState state,
        EntityUid user,
        EntityUid target,
        bool popup,
        TimeSpan delay)
    {
        if (!TryAcquireActionSlot(cross, user, popup))
            return false;

        unchecked
        {
            cross.Comp.ActionId++;
            if (cross.Comp.ActionId == 0)
                cross.Comp.ActionId = 1;
        }

        cross.Comp.ActionState = state;
        cross.Comp.ActiveUser = user;
        cross.Comp.ActiveTarget = target;
        cross.Comp.ActionDeadline = _timing.CurTime + delay + TimeSpan.FromSeconds(1);
        return true;
    }

    private bool TryAcquireActionSlot(Entity<CrossComponent> cross, EntityUid user, bool popup)
    {
        if (cross.Comp.ActionState == CrossActionState.Idle)
            return true;

        if (cross.Comp.ActionDeadline is not { } deadline || _timing.CurTime > deadline)
        {
            ClearAction(cross);
            return true;
        }

        if (popup)
            PopupCrossBusy(cross, user);

        return false;
    }

    private bool TryConsumeDoAfterAction(
        Entity<CrossComponent> cross,
        CrossActionState state,
        uint actionId,
        EntityUid user)
    {
        if (cross.Comp.ActionState != state)
            return false;

        if (cross.Comp.ActionId != actionId)
            return false;

        if (cross.Comp.ActiveUser != user)
            return false;

        ClearAction(cross);
        return true;
    }

    private void ClearAction(Entity<CrossComponent> cross)
    {
        cross.Comp.ActionState = CrossActionState.Idle;
        cross.Comp.ActionDeadline = null;
        cross.Comp.ActiveUser = null;
        cross.Comp.ActiveTarget = null;
    }

    private bool TryStartCrossDoAfter(
        Entity<CrossComponent> cross,
        EntityUid user,
        TimeSpan delay,
        DoAfterEvent doAfterEvent)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, delay, doAfterEvent, cross.Owner, target: cross.Owner)
        {
            BreakOnMove = true,
            BreakOnWeightlessMove = false,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = SharedInteractionSystem.InteractionRange
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }
}
