using Content.Client._Forge.Warfront.CapturePoint.UI;
using Content.Shared._Forge.Warfront.CapturePoint;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Warfront.CapturePoint;

[UsedImplicitly]
public sealed class CapturePointBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private CapturePointWindow? _window;

    public CapturePointBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindowCenteredLeft<CapturePointWindow>();
        _window.CaptureStart += () => SendMessage(new CapturePointStartMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CapturePointBoundUserInterfaceState bState)
            return;

        _window?.UpdateState(bState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _window?.Close();
            _window = null;
        }
    }
}
