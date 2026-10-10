using System.Linq;
using Content.Shared._Forge.Newspapers;
using Robust.Shared.Utility;

namespace Content.Client._Forge.Newspapers;

public sealed partial class NewspaperDeskWindow
{
    private string? _emphasisText;
    private int _emphasisStart = -1;
    private int _emphasisEnd = -1;
    private bool NameAvailable(string name) => name.Trim().Length is >= 1 and <= 80 &&
        (_state == null || !_state.Publications.Any(p => p.Id != _draft.PublicationId &&
            string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)));

    private bool NewNameAvailable(string name) => name.Trim().Length is >= 1 and <= 80 &&
        (_state == null || !_state.Publications.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)));

    private void OpenRename()
    {
        NameField.Text = _draft.Name;
        PublicationNameToolbar.Visible = true;
        NameField.GrabKeyboardFocus();
        Refresh();
    }

    public override void Close()
    {
        if (_state != null && !_discardOnClose && (_save.Saved == null || !SameDraft(_save.Saved, _draft)))
        {
            if (!DraftIsValid() || _save.Conflict)
            {
                _published = false;
                _closeWarning = true;
                Refresh();
                return;
            }
            _closeAfterSave = true;
            if (_save.Pending == null || !SameDraft(_save.Pending, _draft)) SaveIfChanged();
            Refresh();
            return;
        }
        base.Close();
    }

    private void RefreshFeedback(bool valid, bool dirty, bool alreadyPublished)
    {
        if (_state == null) return;
        CurrentTemplateLabel.Visible = !_published;
        CurrentTemplateLabel.SetMessage(Loc.GetString("newspaper-current-template", ("name",
            string.IsNullOrEmpty(_draft.TemplateName) ? Loc.GetString("newspaper-custom-layout") : _draft.TemplateName)));
        if (_armedReplacement != BlankButton) BlankButton.Text = Loc.GetString(_layout ? "newspaper-template-blank" : "newspaper-clear-materials");
        AutoSaveHintLabel.Visible = !_published && dirty;
        DiscardCloseButton.Visible = _closeWarning && dirty && !_published;
        ApplyNameButton.Disabled = _save.Pending != null || _save.Conflict || !valid || !NameAvailable(NameField.Text);
        CreatePublicationButton.Disabled = _save.Pending != null || _save.Conflict || !valid || !NewNameAvailable(NewPublicationName.Text);
        NewPublicationButton.Disabled = _save.Pending != null || _save.Conflict || _published || _state!.Publications.Length >= NewspaperLayout.MaxPublications;

        ReloadDraftButton.Visible = _save.Conflict && !_published;
        string? reason = null;
        if (_published)
        {
            if (_selectedArchiveId > 0 && _state.PaperCount < _printCount) reason = "newspaper-print-paper-tip";
        }
        else if (_closeWarning && !valid) reason = "newspaper-close-invalid";
        else if (_save.Failed) reason = "newspaper-save-failed";
        else if (_closeAfterSave) reason = "newspaper-close-saving";
        else if (PublicationNameToolbar.Visible && !NameAvailable(NameField.Text)) reason = "newspaper-name-error";
        else if (NewPublicationToolbar.Visible && !NewNameAvailable(NewPublicationName.Text)) reason = "newspaper-name-error";
        else if (_draft.Name.Trim().Length > 80) reason = "newspaper-name-error";
        else if (_state.Publications.Any(p => p.Id != _draft.PublicationId && string.Equals(p.Name, _draft.Name.Trim(), StringComparison.OrdinalIgnoreCase))) reason = "newspaper-name-error";
        else if (!valid) reason = "newspaper-layout-error";
        else if (string.IsNullOrWhiteSpace(_draft.Name)) reason = "newspaper-name-required";
        else if (!_draft.Blocks.Any(b => b.Kind == NewspaperBlockKind.Text && !b.IsLabel && !string.IsNullOrWhiteSpace(b.Text))) reason = "newspaper-material-required";
        else if (_save.Publishing) reason = "newspaper-publishing";
        else if (alreadyPublished) reason = "newspaper-publish-unchanged-tip";
        else if (_recoveredDraft) reason = "newspaper-draft-restored";
        reason = _save.Error ?? reason;
        ActionHintLabel.Visible = reason != null;
        ActionHintLabel.SetMessage(reason == null ? "" : Loc.GetString(reason));
        if (_save.Failed) SaveStatusLabel.Text = Loc.GetString("newspaper-save-failed-short");
    }

    private void RefreshEmphasis()
    {
        if (Selected?.Kind != NewspaperBlockKind.Text) return;
        var text = Rope.Collapse(TextField.TextRope);
        var start = TextField.SelectionLower.Index;
        var end = TextField.SelectionUpper.Index;
        if (text == _emphasisText && start == _emphasisStart && end == _emphasisEnd) return;
        _emphasisText = text; _emphasisStart = start; _emphasisEnd = end;
        var styles = NewspaperTypography.SelectedStyles(text, start, end);
        BoldButton.Pressed = styles.Bold;
        ItalicButton.Pressed = styles.Italic;
    }
}
