using Content.Shared._Forge.Newspapers;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Newspapers;

public sealed class NewspaperCopyBoundUserInterface : BoundUserInterface
{
    private NewspaperCopyWindow? _window;

    public NewspaperCopyBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NewspaperCopyWindow>();
        _window.OnImageRequested += id => SendMessage(new NewspaperImageRequestMessage(0, id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NewspaperCopyUiState copyState)
            _window?.UpdateState(copyState);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is NewspaperImageMessage image)
            _window?.ReceivePhoto(image.PhotoId, image.Data);
    }
}
