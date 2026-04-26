using Content.Shared._Forge.Photo;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Photo.UI;

public sealed class PhotoCardBoundUserInterface : BoundUserInterface
{
    private readonly PhotoSystem _photoSystem;

    [ViewVariables]
    private PhotoCardWindow? _window;

    private int _currentImageId = -1;

    public PhotoCardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _photoSystem = EntMan.System<PhotoSystem>();
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PhotoCardWindow>();
        _photoSystem.OnImageReceived += OnImageReceived;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not PhotoCardUiState cast)
            return;

        if (cast.ImageId == -1)
            return;

        _currentImageId = cast.ImageId;

        // Check client-side cache first.
        var cached = _photoSystem.GetCachedImage(cast.ImageId);
        if (cached != null)
        {
            _window.ShowImage(cached);
            return;
        }

        // Not cached — request from server.
        _photoSystem.RequestImage(cast.ImageId);
    }

    private void OnImageReceived(int imageId, byte[] data)
    {
        if (_window != null && imageId == _currentImageId)
            _window.ShowImage(data);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _photoSystem.OnImageReceived -= OnImageReceived;
    }
}
