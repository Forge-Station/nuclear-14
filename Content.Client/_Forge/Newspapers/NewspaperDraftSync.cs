using Content.Shared._Forge.Newspapers;

namespace Content.Client._Forge.Newspapers;

/// <summary>Decides whether a server update can replace local edits. A save acknowledgement
/// establishes a baseline; it never substitutes its snapshot for the current editor text.</summary>
internal static class NewspaperDraftSync
{
    public static bool CanApply(NewspaperEdition? saved, NewspaperEdition local, bool initial, bool pending) =>
        initial ||
        !pending && saved != null && NewspaperStorage.SameContent(saved, local);

    public static bool IsAcknowledgement(int request, int pendingRequest, NewspaperEdition? snapshot) =>
        request != 0 && request == pendingRequest && snapshot != null;
}

/// <summary>Owns the saved baseline and the lifetime of a draft request. Window visibility,
/// local typing and close confirmation remain the editor's responsibility.</summary>
internal sealed class NewspaperDraftSaveState
{
    private int _nextRequestId;
    private int _pendingRequestId;
    private bool _switching;
    private TimeSpan _startedAt;

    public long Revision { get; private set; }
    public NewspaperEdition? Saved { get; private set; }
    public NewspaperEdition? Pending { get; private set; }
    public NewspaperDraftResultMessage? DeferredResult { get; private set; }
    public bool Publishing { get; private set; }
    public bool Failed { get; private set; }
    public bool Conflict { get; private set; }
    public string? Error { get; private set; }

    public void AcceptBaseline(NewspaperEdition draft, long revision)
    {
        Saved = draft.Clone();
        Revision = revision;
    }

    public void AdvanceRevision(long revision) => Revision = revision;

    public void MarkConflict()
    {
        Conflict = true;
        Error = "newspaper-save-conflict";
    }

    public void Restore(NewspaperEdition baseline, long revision, bool conflict)
    {
        AcceptBaseline(baseline, revision);
        Conflict = conflict;
        Error = conflict ? "newspaper-save-conflict" : null;
    }

    public void Reload(NewspaperEdition draft, long revision)
    {
        Restore(draft, revision, false);
        Failed = false;
    }

    public void Prepare(NewspaperEdition draft, TimeSpan now, bool publishing = false)
    {
        Pending = draft.Clone();
        Publishing = publishing;
        Failed = false;
        _startedAt = now;
    }

    public int BeginRequest(NewspaperEdition draft, TimeSpan now, bool switching)
    {
        Pending = draft.Clone();
        _switching = switching;
        Error = null;
        _startedAt = now;
        return _pendingRequestId = ++_nextRequestId;
    }

    public bool TryComplete(NewspaperDraftResultMessage result, long? serverRevision,
        out (NewspaperEdition Saved, bool Switching, bool Publishing) completion)
    {
        completion = default;
        if (!NewspaperDraftSync.IsAcknowledgement(result.RequestId, _pendingRequestId, Pending))
            return false;

        // A switch acknowledgement may arrive before the UI state containing the target draft.
        if (result.Error.Length == 0 && _switching && (serverRevision == null || serverRevision < result.Revision))
        {
            DeferredResult = result;
            return false;
        }

        completion = (Pending!, _switching, Publishing);
        ClearRequest();
        if (result.Error.Length > 0)
        {
            Error = result.Error;
            Conflict = result.Error == "newspaper-save-conflict";
            Failed = true;
        }
        else
        {
            Saved = completion.Saved;
            Revision = result.Revision;
            Conflict = false;
            Failed = false;
        }
        return true;
    }

    public bool TryTimeout(TimeSpan now)
    {
        if (Pending == null && !Publishing || now - _startedAt <= TimeSpan.FromSeconds(5))
            return false;

        ClearRequest();
        Failed = true;
        return true;
    }

    private void ClearRequest()
    {
        Pending = null;
        _pendingRequestId = 0;
        _switching = false;
        Publishing = false;
        DeferredResult = null;
    }
}
