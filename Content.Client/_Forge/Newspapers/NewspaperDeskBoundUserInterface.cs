using Content.Shared._Forge.Newspapers;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Newspapers;

public sealed class NewspaperDeskBoundUserInterface : BoundUserInterface
{
    private NewspaperDeskWindow? _window;
    private readonly NetEntity _desk;

    public NewspaperDeskBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        // The server identity survives the desk leaving and re-entering the client's PVS.
        _desk = EntMan.GetNetEntity(owner);
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NewspaperDeskWindow>();
        _window.OnArchiveRequested += index => SendMessage(new NewspaperArchiveRequestMessage(index));
        _window.OnDeleteEdition += id => SendMessage(new NewspaperDeleteEditionMessage(id));
        _window.OnSaveTemplate += template => SendMessage(new NewspaperSaveTemplateMessage(template));
        _window.OnSwitchPublication += (draft, id, name) => SendMessage(new NewspaperSwitchPublicationMessage(draft, id, name, _window.Revision, _window.BeginRequest(draft, true)));
        _window.OnSave += draft => SendMessage(new NewspaperSaveDraftMessage(draft, _window.Revision, _window.BeginRequest(draft)));
        _window.OnPublish += draft => SendMessage(new NewspaperPublishMessage(draft, _window.Revision, _window.BeginRequest(draft)));
        _window.OnPrint += (count, edition, paper) => SendMessage(new NewspaperPrintMessage(count, edition, paper));
        _window.OnImageRequested += (edition, id) => SendMessage(new NewspaperImageRequestMessage(edition, id));
        _window.OnSelectPhoto += id => SendMessage(new NewspaperSelectImageMessage(id));
        _window.OnForgetPhoto += id => SendMessage(new NewspaperForgetImageMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NewspaperDeskUiState deskState)
        {
            _window?.UpdateState(deskState);
            if (_window != null && EntMan.System<NewspaperDraftRecoverySystem>()
                .TryTake(_desk, deskState.Draft.PublicationId, out var recovery) && recovery != null)
                _window.RestoreRecovery(recovery);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null)
            EntMan.System<NewspaperDraftRecoverySystem>().Store(_desk, _window.PublicationId, _window.CaptureRecovery());
        base.Dispose(disposing);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is NewspaperDraftResultMessage result) _window?.ReceiveResult(result);
        if (message is NewspaperArchiveMessage archive) _window?.ReceiveArchive(archive);
        if (message is NewspaperImageMessage image)
            _window?.ReceivePhoto(image.PhotoId, image.Data, image.PhotoKey);
    }
}
