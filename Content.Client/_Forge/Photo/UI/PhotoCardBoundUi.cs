using Content.Shared._Forge.Photo;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Photo.UI;

public sealed class PhotoCardBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PhotoCardWindow? _window;

    public PhotoCardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PhotoCardWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not PhotoCardUiState cast)
            return;

        if (cast.ImageData == null)
            return;

        _window.ShowImage(cast.ImageData);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
