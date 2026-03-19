using Content.Shared._Forge.Cross;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;

namespace Content.Server._Forge.Cross;

public sealed partial class CrossServerSystem
{
    private bool CanHangNow(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!ValidateHangEntities(cross, user, target))
            return false;

        if (!ValidateTargetIsNotCross(cross, target))
            return false;

        if (!ValidateTargetNotAlreadyHung(cross, user, target, popup))
            return false;

        if (!ValidateTargetHasHands(cross, user, target, popup))
            return false;

        if (!ValidateCrossNotOccupiedByOtherTarget(cross, user, target, popup))
            return false;

        if (!ValidateSameContainerForHang(cross, target))
            return false;

        if (!ValidateHangRange(cross, user, target, popup))
            return false;

        return true;
    }

    private bool CanUnhangNow(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!ValidateUnhangEntities(cross, user, target))
            return false;

        if (!ValidateUnhangTargetMatchesCurrentHung(cross, user, target, popup))
            return false;

        if (!ValidateUnhangRange(cross, user, target, popup))
            return false;

        return true;
    }

    private bool ValidateHangEntities(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        return Exists(user)
               && Exists(target)
               && !TerminatingOrDeleted(target)
               && !TerminatingOrDeleted(cross.Owner);
    }

    private static bool ValidateTargetIsNotCross(Entity<CrossComponent> cross, EntityUid target)
    {
        return target != cross.Owner;
    }

    private bool ValidateTargetNotAlreadyHung(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!TryComp<HungOnCrossComponent>(target, out var hung) || hung.Cross is not { })
            return true;

        if (popup)
            PopupHangFail(cross, user, target);

        return false;
    }

    private bool ValidateTargetHasHands(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (TryComp<HandsComponent>(target, out var hands) && hands.Count > 0)
            return true;

        if (popup)
            PopupHangFail(cross, user, target);

        return false;
    }

    private bool ValidateCrossNotOccupiedByOtherTarget(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!TryGetHungTarget(cross, out var currentTarget) || currentTarget == target)
            return true;

        if (popup)
            PopupCrossBusy(cross, user);

        return false;
    }

    private bool ValidateSameContainerForHang(Entity<CrossComponent> cross, EntityUid target)
    {
        return _container.IsInSameOrNoContainer((target, null, null), (cross.Owner, null, null));
    }

    private bool ValidateHangRange(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        var inRange = _interaction.InRangeUnobstructed(
            target,
            cross.Owner,
            predicate: ent => IgnoreCrossHangObstruction(ent, user, target, cross.Owner),
            popup: false);

        if (!inRange && popup)
            PopupHangFail(cross, user, target);

        return inRange;
    }

    private bool ValidateUnhangEntities(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        return Exists(user)
               && Exists(target)
               && !TerminatingOrDeleted(cross.Owner);
    }

    private bool ValidateUnhangTargetMatchesCurrentHung(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (TryGetHungTarget(cross, out var currentTarget) && currentTarget == target)
            return true;

        if (popup)
            PopupUnhangFail(cross, user, target);

        return false;
    }

    private bool ValidateUnhangRange(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        var inRange = _interaction.InRangeUnobstructed(user, cross.Owner, popup: false);

        if (!inRange && popup)
            PopupUnhangFail(cross, user, target);

        return inRange;
    }

    private static bool IgnoreCrossHangObstruction(EntityUid ent, EntityUid user, EntityUid target, EntityUid cross)
    {
        return ent == user || ent == target || ent == cross;
    }
}

