using Content.Shared._Forge.Cross;

namespace Content.Server._Forge.Cross;

public sealed partial class CrossServerSystem
{
    private void PopupCrossBusy(Entity<CrossComponent> cross, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-busy"), cross.Owner, user);
    }

    private void PopupHangStart(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-start", ("target", target)), cross.Owner, user);
    }

    private void PopupSelfUnhangDenied(Entity<CrossComponent> cross, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-self-unhang-denied"), cross.Owner, user);
    }

    private void PopupUnhangStart(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-start", ("target", target)), cross.Owner, user);
    }

    private void PopupHangFail(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-fail", ("target", target)), cross.Owner, user);
    }

    private void PopupUnhangFail(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-fail", ("target", target)), cross.Owner, user);
    }

    private void PopupHangSuccess(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-success-user", ("target", target)), cross.Owner, user);

        if (target != user)
            _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-success-target", ("user", user)), cross.Owner, target);
    }

    private void PopupUnhangSuccess(Entity<CrossComponent> cross, EntityUid user, EntityUid target)
    {
        _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-success", ("target", target)), cross.Owner, user);
    }
}