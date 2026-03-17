using Content.Shared._Forge.Cross;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;

namespace Content.Server._Forge.Cross;

public sealed partial class CrossServerSystem
{
    private void OnInteractHand(Entity<CrossComponent> cross, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;


        if (!_pulling.TryGetPulledEntity(args.User, out var pulledTarget) || pulledTarget is not { } target)
            return;

        args.Handled = true;
        TryStartHangAction(cross, args.User, target, popup: true);
    }

    private void OnDragDropTarget(Entity<CrossComponent> cross, ref DragDropTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartHangAction(cross, args.User, args.Dragged, popup: false);
    }

    private bool TryStartHangAction(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!CanHangNow(cross, user, target, popup))
            return false;

        if (!TryBeginAction(cross, CrossActionState.HangPending, user, target, popup, cross.Comp.HangDelay))
            return false;

        if (!TryStartHangDoAfter(cross, user, target))
        {
            ClearAction(cross);
            return false;
        }

        if (popup)
            PopupHangStart(cross, user, target);

        return true;
    }

    private bool TryStartUnhangAction(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (target == user)
        {
            if (popup)
                PopupSelfUnhangDenied(cross, user);
            return false;
        }

        if (!CanUnhangNow(cross, user, target, popup))
            return false;

        if (!TryBeginAction(cross, CrossActionState.UnhangPending, user, target, popup, cross.Comp.UnhangDelay))
            return false;

        if (!TryStartUnhangDoAfter(cross, user, target))
        {
            ClearAction(cross);
            return false;
        }

        if (popup)
            PopupUnhangStart(cross, user, target);

        return true;
    }

    private void OnHangDoAfter(Entity<CrossComponent> cross, ref CrossHangDoAfterEvent args)
    {
        var user = args.Args.User;
        var target = GetEntity(args.HangTarget);

        if (!TryPrepareDoAfter(
                cross,
                CrossActionState.HangPending,
                args.ActionId,
                user,
                target,
                args.Handled,
                args.Cancelled,
                "n14-cross-popup-hang-interrupted"))
            return;

        if (!TryCompleteHang(cross, user, target))
        {
            PopupHangFail(cross, user, target);
            return;
        }

        PopupHangSuccess(cross, user, target);
        args.Handled = true;
    }

    private void OnUnhangDoAfter(Entity<CrossComponent> cross, ref CrossUnhangDoAfterEvent args)
    {
        var user = args.Args.User;
        var target = GetEntity(args.UnhangTarget);

        if (!TryPrepareDoAfter(
                cross,
                CrossActionState.UnhangPending,
                args.ActionId,
                user,
                target,
                args.Handled,
                args.Cancelled,
                "n14-cross-popup-unhang-interrupted"))
            return;

        if (!TryCompleteUnhang(cross, user, target))
        {
            PopupUnhangFail(cross, user, target);
            return;
        }

        PopupUnhangSuccess(cross, user, target);
        args.Handled = true;
    }

    private bool TryCompleteHang(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        return CanHangNow(cross, user, target, popup: false)
               && TryHangTarget(cross, target, user);
    }

    private bool TryCompleteUnhang(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        return CanUnhangNow(cross, user, target, popup: false)
               && TryUnhangTarget(cross, target, applyBreakEffects: false);
    }

    private bool TryPrepareDoAfter(
        Entity<CrossComponent> cross,
        CrossActionState state,
        uint actionId,
        EntityUid user,
        EntityUid target,
        bool handled,
        bool cancelled,
        string interruptionLoc)
    {
        if (!TryConsumeDoAfterAction(cross, state, actionId, user))
            return false;

        if (HandleDoAfterInterruption(cross, handled, cancelled, user, interruptionLoc))
            return false;

        return !AreDoAfterParticipantsInvalid(user, target);
    }
}

