using System.Linq;
using Content.Shared._Forge.Newspapers;

namespace Content.Client._Forge.Newspapers;

public sealed partial class NewspaperDeskWindow
{
    private readonly NewspaperDraftSaveState _save = new();
    private bool _recoveredDraft;
    private NewspaperEdition? _loadedEdition;
    private int _loadedArchiveId;
    private int _requestedArchiveId;
    private TimeSpan _archiveRequestTime;
    private Dictionary<int, string> _archivePhotoKeys = new();
    public long Revision => _save.Revision;
    public event Action<int>? OnArchiveRequested;
    public event Action<int>? OnDeleteEdition;

    internal int PublicationId => _draft.PublicationId;

    internal NewspaperDraftRecovery? CaptureRecovery() => _discardOnClose || _state == null || _save.Saved == null ||
        SameDraft(_save.Saved, _draft) ? null : new(_draft.Clone(), _save.Saved.Clone(), _save.Pending?.Clone(), _save.Revision);

    internal void RestoreRecovery(NewspaperDraftRecovery recovery)
    {
        if (_state == null || SameDraft(recovery.Draft, _state.Draft)) return;
        var safe = recovery.CanRestore(_state.Draft);
        _save.Restore(safe ? _state.Draft : recovery.Baseline,
            safe ? _state.Revision : recovery.Revision, !safe);
        _recoveredDraft = true;
        _undo.Clear(); _redo.Clear(); _checkpoint = null;
        var draft = recovery.Draft.Clone();
        NewspaperStorage.RemoveUnavailablePhotos(draft, _state.BufferedPhotos.Concat(_state.Draft.PhotoIds));
        SetDraft(draft);
    }

    public int BeginRequest(NewspaperEdition draft, bool switching = false)
    {
        _recoveredDraft = false;
        return _save.BeginRequest(draft, _timing.RealTime, switching);
    }

    public void ReceiveResult(NewspaperDraftResultMessage result)
    {
        if (!_save.TryComplete(result, _state?.Revision, out var completion))
            return;

        if (result.Error.Length > 0)
        {
            _closeWarning |= _closeAfterSave;
            _closeAfterSave = false;
        }
        else
        {
            if (completion.Switching)
            {
                if (_state?.Revision == result.Revision && SameDraft(completion.Saved, _draft))
                {
                    _save.AcceptBaseline(_state.Draft, result.Revision);
                    _undo.Clear(); _redo.Clear(); _checkpoint = null;
                    SetDraft(_state.Draft.Clone());
                    UpdateState(_state);
                }
                else
                    _save.MarkConflict();
            }
            if (_state != null && _state.Revision > result.Revision && !SameDraft(_state.Draft, _save.Saved!))
                _save.MarkConflict();

            if (completion.Publishing)
            {
                _published = true;
                _layout = false;
            }
        }
        Refresh();
        if (!_closeAfterSave) return;
        if (SameDraft(_save.Saved!, _draft)) { _closeAfterSave = false; base.Close(); }
        else SaveIfChanged();
    }

    private void ReloadServerDraft()
    {
        if (_state == null) return;
        _save.Reload(_state.Draft, _state.Revision);
        _recoveredDraft = false;
        _undo.Clear(); _redo.Clear(); _checkpoint = null;
        SetDraft(_state.Draft.Clone());
    }

    public void ReceiveArchive(NewspaperArchiveMessage message)
    {
        if (message.Index != _selectedArchiveId) return;
        _loadedArchiveId = message.Index;
        _loadedEdition = message.Edition;
        _archivePhotoKeys = message.PhotoKeys;
        Refresh();
    }

    private Dictionary<int, string> VisiblePhotoKeys()
    {
        if (_state == null) return new();
        var keys = _published ? _archivePhotoKeys : _state.PhotoKeys;
        var ids = Displayed.PhotoIdsForPage(_page);
        if (!_published && PhotoBufferButton.Pressed) ids = ids.Concat(_visibleGalleryPhotos);
        return ids.Distinct().Where(keys.ContainsKey).ToDictionary(id => id, id => keys[id]);
    }

    private void PublishDraft()
    {
        _save.Prepare(_draft, _timing.RealTime, publishing: true);
        OnPublish?.Invoke(_draft.Clone());
        Refresh();
    }
}
