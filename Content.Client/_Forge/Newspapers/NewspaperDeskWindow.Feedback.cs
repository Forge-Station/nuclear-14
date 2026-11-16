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
            if (_save.Pending == null || !SameDraft(_save.Pending, _draft))
                SaveIfChanged();
            Refresh();
            return;
        }
        base.Close();
    }

    private void RefreshFeedback(bool valid, bool dirty, bool alreadyPublished)
    {
        if (_state == null)
            return;

        CurrentTemplateLabel.Visible = !_published;
        CurrentTemplateLabel.SetMessage(Loc.GetString("newspaper-current-template", ("name",
            string.IsNullOrEmpty(_draft.TemplateName) ? Loc.GetString("newspaper-custom-layout") : _draft.TemplateName)));
        if (_armedReplacement != BlankButton)
            BlankButton.Text = Loc.GetString(_layout ? "newspaper-template-blank" : "newspaper-clear-materials");
        AutoSaveHintLabel.Visible = !_published && dirty;
        DiscardCloseButton.Visible = _closeWarning && dirty && !_published;
        ApplyNameButton.Disabled = _save.Pending != null || _save.Conflict || !valid || !NameAvailable(NameField.Text);
        CreatePublicationButton.Disabled = _save.Pending != null || _save.Conflict || !valid || !NewNameAvailable(NewPublicationName.Text);
        NewPublicationButton.Disabled = _save.Pending != null || _save.Conflict || _published || _state!.Publications.Length >= NewspaperLayout.MaxPublications;

        ReloadDraftButton.Visible = _save.Conflict && !_published;
        var reason = GetActionHint(valid, alreadyPublished);
        ActionHintLabel.Visible = reason != null;
        ActionHintLabel.SetMessage(reason == null ? "" : Loc.GetString(reason));
        if (_save.Failed)
            SaveStatusLabel.Text = Loc.GetString("newspaper-save-failed-short");
    }

    private string? GetActionHint(bool valid, bool alreadyPublished)
    {
        if (_state == null)
            return null;

        return true switch
        {
            _ when _save.Error != null => _save.Error,

            _ when _published && _selectedArchiveId > 0 && _state.PaperCount < _printCount =>
                "newspaper-print-paper-tip",
            _ when _published => null,

            _ when _closeWarning && !valid => "newspaper-close-invalid",
            _ when _save.Failed => "newspaper-save-failed",
            _ when _closeAfterSave => "newspaper-close-saving",

            _ when PublicationNameToolbar.Visible && !NameAvailable(NameField.Text) =>
                "newspaper-name-error",
            _ when NewPublicationToolbar.Visible && !NewNameAvailable(NewPublicationName.Text) =>
                "newspaper-name-error",
            _ when _draft.Name.Trim().Length > 80 => "newspaper-name-error",
            _ when _state.Publications.Any(p => p.Id != _draft.PublicationId &&
                string.Equals(p.Name, _draft.Name.Trim(), StringComparison.OrdinalIgnoreCase)) =>
                "newspaper-name-error",

            _ when !valid => "newspaper-layout-error",
            _ when string.IsNullOrWhiteSpace(_draft.Name) => "newspaper-name-required",
            _ when !_draft.Blocks.Any(b => b.Kind == NewspaperBlockKind.Text &&
                !b.IsLabel && !string.IsNullOrWhiteSpace(b.Text)) =>
                "newspaper-material-required",

            _ when _save.Publishing => "newspaper-publishing",
            _ when alreadyPublished => "newspaper-publish-unchanged-tip",
            _ when _recoveredDraft => "newspaper-draft-restored",
            _ => null,
        };
    }

    private void RefreshEmphasis()
    {
        if (Selected?.Kind != NewspaperBlockKind.Text)
            return;

        var text = Rope.Collapse(TextField.TextRope);
        var start = TextField.SelectionLower.Index;
        var end = TextField.SelectionUpper.Index;
        if (text == _emphasisText && start == _emphasisStart && end == _emphasisEnd)
            return;

        _emphasisText = text;
        _emphasisStart = start;
        _emphasisEnd = end;
        var styles = NewspaperTypography.SelectedStyles(text, start, end);
        BoldButton.Pressed = styles.Bold;
        ItalicButton.Pressed = styles.Italic;
    }
}
