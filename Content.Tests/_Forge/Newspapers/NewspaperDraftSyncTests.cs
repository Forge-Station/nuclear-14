using System;
using Content.Client._Forge.Newspapers;
using Content.Shared._Forge.Newspapers;
using NUnit.Framework;

namespace Content.Tests._Forge.Newspapers;

[TestFixture]
public sealed class NewspaperDraftSyncTests
{
    [Test]
    public void AcknowledgingAnEarlierSaveCannotReplaceNewerLocalTyping()
    {
        var sent = new NewspaperEdition { Name = "Saved A" };
        var local = sent.Clone(); local.Name = "Typed B";
        Assert.That(NewspaperDraftSync.IsAcknowledgement(1, 1, sent), Is.True);
        // A is now the saved baseline; B must remain in the editor after subsequent UI states.
        Assert.That(NewspaperDraftSync.CanApply(sent, local, false, false), Is.False);
        Assert.That(NewspaperDraftSync.IsAcknowledgement(1, 2, local), Is.False);
    }

    [Test]
    public void ConcurrentServerEditsOnlyReplaceACleanEditor()
    {
        var saved = new NewspaperEdition { Name = "A" };
        Assert.That(NewspaperDraftSync.CanApply(saved, saved.Clone(), false, false), Is.True);
        var dirty = saved.Clone(); dirty.Name = "B";
        Assert.That(NewspaperDraftSync.CanApply(saved, dirty, false, false), Is.False);
        Assert.That(NewspaperDraftSync.CanApply(saved, saved, false, true), Is.False);
    }

    [Test]
    public void CompletedSaveKeepsTheSentSnapshotAndRejectsDuplicateAcknowledgements()
    {
        var state = new NewspaperDraftSaveState();
        var local = new NewspaperEdition { Name = "Sent" };
        state.Prepare(local, TimeSpan.Zero);
        var id = state.BeginRequest(local, TimeSpan.Zero, false);
        local.Name = "New typing";

        var result = new NewspaperDraftResultMessage(id, 2, "");
        Assert.That(state.TryComplete(result, 2, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(state.Saved!.Name, Is.EqualTo("Sent"));
            Assert.That(local.Name, Is.EqualTo("New typing"));
            Assert.That(state.Revision, Is.EqualTo(2));
            Assert.That(state.Pending, Is.Null);
            Assert.That(NewspaperDraftSync.CanApply(state.Saved, local, false, false), Is.False);
        });
        Assert.That(state.TryComplete(result, 2, out _), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PublicationSwitchWaitsForTheTargetUiState(bool acknowledgementFirst)
    {
        var state = new NewspaperDraftSaveState();
        var id = state.BeginRequest(new NewspaperEdition { PublicationId = 1 }, TimeSpan.Zero, true);
        var result = new NewspaperDraftResultMessage(id, 3, "");
        if (acknowledgementFirst)
        {
            Assert.That(state.TryComplete(result, 2, out _), Is.False);
            Assert.That(state.DeferredResult, Is.SameAs(result));
            Assert.That(state.Pending, Is.Not.Null);
        }
        Assert.That(state.TryComplete(result, 3, out var completion), Is.True);
        Assert.That(completion.Switching, Is.True);
        Assert.That(state.DeferredResult, Is.Null);
    }

    [Test]
    public void TimedOutRequestCannotCompleteANewerRetry()
    {
        var state = new NewspaperDraftSaveState();
        var draft = new NewspaperEdition { Name = "A" };
        state.Prepare(draft, TimeSpan.Zero, publishing: true);
        var oldId = state.BeginRequest(draft, TimeSpan.Zero, true);
        state.TryComplete(new NewspaperDraftResultMessage(oldId, 3, ""), 2, out _);
        Assert.That(state.TryTimeout(TimeSpan.FromSeconds(5)), Is.False);
        Assert.That(state.TryTimeout(TimeSpan.FromSeconds(6)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(state.Failed, Is.True);
            Assert.That(state.Pending, Is.Null);
            Assert.That(state.Publishing, Is.False);
            Assert.That(state.DeferredResult, Is.Null);
        });

        state.Prepare(draft, TimeSpan.FromSeconds(7));
        var newId = state.BeginRequest(draft, TimeSpan.FromSeconds(7), false);
        Assert.That(state.TryComplete(new NewspaperDraftResultMessage(oldId, 3, ""), 3, out _), Is.False);
        Assert.That(state.TryComplete(new NewspaperDraftResultMessage(newId, 4, ""), 4, out _), Is.True);
        Assert.That(state.Failed, Is.False);
    }

    [TestCase("newspaper-save-conflict", true)]
    [TestCase("newspaper-request-wait", false)]
    public void RejectedPublishPreservesBaselineAndReportsTheServerError(string error, bool conflict)
    {
        var state = new NewspaperDraftSaveState();
        state.AcceptBaseline(new NewspaperEdition { Name = "Saved" }, 1);
        var draft = new NewspaperEdition { Name = "Rejected" };
        state.Prepare(draft, TimeSpan.Zero, publishing: true);
        var id = state.BeginRequest(draft, TimeSpan.Zero, false);
        Assert.That(state.TryComplete(new NewspaperDraftResultMessage(id, 2, error), 2, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(state.Saved!.Name, Is.EqualTo("Saved"));
            Assert.That(state.Revision, Is.EqualTo(1));
            Assert.That(state.Failed, Is.True);
            Assert.That(state.Conflict, Is.EqualTo(conflict));
            Assert.That(state.Error, Is.EqualTo(error));
            Assert.That(state.Publishing, Is.False);
        });
    }

    [Test]
    public void SuccessfulPublishIsReportedOnce()
    {
        var state = new NewspaperDraftSaveState();
        var draft = new NewspaperEdition { Name = "Published" };
        state.Prepare(draft, TimeSpan.Zero, publishing: true);
        var id = state.BeginRequest(draft, TimeSpan.Zero, false);
        var result = new NewspaperDraftResultMessage(id, 2, "");
        Assert.That(state.TryComplete(result, 2, out var completion), Is.True);
        Assert.That(completion.Publishing, Is.True);
        Assert.That(state.Publishing, Is.False);
        Assert.That(state.TryComplete(result, 2, out _), Is.False);
    }

    [Test]
    public void ReloadClearsRecoveredConflictAndClonesTheServerBaseline()
    {
        var state = new NewspaperDraftSaveState();
        state.Restore(new NewspaperEdition { Name = "Old" }, 1, true);
        Assert.That(state.Conflict, Is.True);
        var server = new NewspaperEdition { Name = "Server" };
        state.Reload(server, 3);
        server.Name = "Later";
        Assert.Multiple(() =>
        {
            Assert.That(state.Saved!.Name, Is.EqualTo("Server"));
            Assert.That(state.Revision, Is.EqualTo(3));
            Assert.That(state.Conflict, Is.False);
            Assert.That(state.Failed, Is.False);
            Assert.That(state.Error, Is.Null);
        });
    }
}
