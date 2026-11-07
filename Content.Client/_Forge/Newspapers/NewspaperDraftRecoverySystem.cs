using Content.Shared._Forge.Newspapers;
using Content.Shared.GameTicking;
using System.Linq;

namespace Content.Client._Forge.Newspapers;

/// <summary>Unsaved text survives BUI disposal, without sending messages to an already closed server UI.</summary>
public sealed class NewspaperDraftRecoverySystem : EntitySystem
{
    private readonly Dictionary<(NetEntity Desk, int Publication), NewspaperDraftRecovery> _drafts = new();
    private const int MaxDrafts = 64;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _drafts.Clear());
    }

    internal void Store(NetEntity desk, int publication, NewspaperDraftRecovery? recovery)
    {
        _drafts.Remove((desk, publication));
        if (recovery == null) return;
        if (_drafts.Count >= MaxDrafts) _drafts.Remove(_drafts.Keys.First());
        _drafts[(desk, publication)] = recovery;
    }

    internal bool TryTake(NetEntity desk, int publication, out NewspaperDraftRecovery? recovery) =>
        _drafts.Remove((desk, publication), out recovery);

    public override void Shutdown()
    {
        _drafts.Clear();
        base.Shutdown();
    }
}

internal sealed record NewspaperDraftRecovery(NewspaperEdition Draft, NewspaperEdition Baseline,
    NewspaperEdition? Pending, long Revision)
{
    public bool CanRestore(NewspaperEdition server) => NewspaperStorage.SameContent(server, Baseline) ||
        Pending != null && NewspaperStorage.SameContent(server, Pending);
}
