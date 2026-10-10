using System;
using System.Linq;
using Content.Client._Forge.Newspapers;
using Content.Client._Forge.Paper;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using System.Collections.Generic;
using Content.Shared._Forge.Newspapers;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._Forge.Newspapers;

[TestFixture]
public sealed class NewspaperRegressionTests
{
    private sealed class TestTexture() : Texture(new Vector2i(1, 1)), IDisposable
    {
        public override Color GetPixel(int x, int y) => Color.White;
        public void Dispose() {}
    }

    [Test]
    public void ChangingSidesReleasesPinnedPhotosAndAdmitsTheOtherSide()
    {
        using var cache = new DocumentResourceCache<DocumentTextures>(64L * 1024 * 1024);
        using var photos = new NewspaperPhotos((key, _) =>
            cache.Acquire(key, 4L * 1024 * 1024, () => new DocumentTextures(new TestTexture())));
        var firstSide = Enumerable.Range(0, 16).ToDictionary(id => id, id => $"photo:{id}");
        foreach (var (id, key) in firstSide) Assert.That(photos.Get(id, key), Is.Not.Null);
        Assert.That(photos.Get(16, "photo:16"), Is.Null);
        var secondSide = new Dictionary<int, string> { [16] = "photo:16" };
        photos.Retain(secondSide);
        Assert.That(photos.Get(16, "photo:16"), Is.Not.Null);
        Assert.That(photos.IsResolved(0), Is.False);
        Assert.That(cache.Bytes, Is.LessThanOrEqualTo(64L * 1024 * 1024));
    }

    [Test]
    public void RemovingAnArchiveEntryDoesNotRetargetAnotherEntryOrAPrintedSnapshot()
    {
        var first = Draft("first"); first.ArchiveId = 11; first.Number = 1;
        var second = Draft("second"); second.ArchiveId = 12; second.Number = 2;
        var archive = new List<NewspaperEdition> { first, second };
        var printed = second;
        var request = new NewspaperDeleteEditionMessage(12);
        Assert.That(NewspaperStorage.TryRemoveEdition(archive, first.ArchiveId, out _), Is.True);
        Assert.That(archive.Single(e => e.ArchiveId == request.Edition), Is.SameAs(second));
        Assert.That(NewspaperStorage.TryRemoveEdition(archive, request.Edition, out var removed), Is.True);
        Assert.That(removed, Is.SameAs(second));
        Assert.That(NewspaperStorage.TryRemoveEdition(archive, request.Edition, out _), Is.False);
        Assert.That(NewspaperStorage.TryRemoveEdition(archive, 0, out _), Is.False);
        Assert.That(archive.Any(e => e.ArchiveId == request.Edition), Is.False);
        Assert.That(printed.Blocks[0].Text, Is.EqualTo("second"));
        Assert.That(NewspaperStorage.Summary(printed).ArchiveId, Is.EqualTo(12));
        Assert.That(printed.Clone().ArchiveId, Is.EqualTo(12));
    }

    [Test]
    public void DeletedIssueNumbersAreNotReusedAndDeletionFreesTheArchiveLimit()
    {
        var edition = Draft("archive"); edition.Number = 9;
        Assert.That(NewspaperLayout.NextEditionNumber(Array.Empty<NewspaperEdition>(), 1, 9), Is.EqualTo(10));
        Assert.That(NewspaperLayout.NextEditionNumber(new[] { edition }, 1, 8), Is.EqualTo(10));
        Assert.That(NewspaperLayout.NextEditionNumber(new[] { edition }, 2, 3), Is.EqualTo(4));
        var full = Enumerable.Range(1, NewspaperStorage.MaxEditions).Select(i => {
            var item = Draft($"old {i}"); item.Number = i; return item;
        }).ToList();
        var draft = Draft("new article");
        Assert.That(NewspaperStorage.CanPublish(full, draft, TimeSpan.FromSeconds(1), TimeSpan.Zero), Is.False);
        full.RemoveAt(0);
        Assert.That(NewspaperStorage.CanPublish(full, draft, TimeSpan.FromSeconds(1), TimeSpan.Zero), Is.True);
    }

    [Test]
    public void NullDraftAndNestedNullsAreRejectedWithoutThrowing()
    {
        Assert.That(NewspaperLayout.IsValid(null), Is.False);
        Assert.That(NewspaperLayout.IsValid(new() { Blocks = null! }), Is.False);
        Assert.That(NewspaperLayout.IsValid(new() { Blocks = new() { null! } }), Is.False);
        Assert.That(NewspaperLayout.IsValid(new() { Name = null! }), Is.False);
    }

    [Test]
    public void RequestsOnlyContainPhotosOnTheCurrentSideIncludingLegacyAssignments()
    {
        var edition = new NewspaperEdition { Pages = 2, PhotoId = 5, Blocks = new() {
            new() { Kind = NewspaperBlockKind.Photo, Page = 0, PhotoId = -2 },
            new() { Kind = NewspaperBlockKind.Photo, Page = 0, PhotoId = 7 },
            new() { Kind = NewspaperBlockKind.Photo, Page = 0, PhotoId = 7 },
            new() { Kind = NewspaperBlockKind.Photo, Page = 1, PhotoId = 9 },
            new() { Kind = NewspaperBlockKind.Photo, Page = 1, PhotoId = -1 },
        } };
        Assert.That(edition.PhotoIdsForPage(0), Is.EquivalentTo(new[] { 5, 7 }));
        Assert.That(edition.PhotoIdsForPage(1), Is.EquivalentTo(new[] { 9 }));
        edition.Blocks.RemoveAt(0);
        Assert.That(edition.PhotoIdsForPage(0), Does.Not.Contain(5));
    }

    [Test]
    public void RecoveryAllowsUnchangedServerOrDeliveredPendingSaveButRejectsOtherEdits()
    {
        var baseline = Draft("old");
        var pending = Draft("first edit");
        var recovery = new NewspaperDraftRecovery(Draft("latest edit"), baseline, pending, 4);
        Assert.That(recovery.CanRestore(baseline.Clone()), Is.True);
        Assert.That(recovery.CanRestore(pending.Clone()), Is.True);
        Assert.That(recovery.CanRestore(Draft("someone else")), Is.False);
        Assert.That(recovery.CanRestore(new() { PublicationId = 2 }), Is.False);
    }

    [Test]
    public void RecoveryIsIsolatedByDeskAndPublicationAndConsumedOnce()
    {
        var system = new NewspaperDraftRecoverySystem();
        var recovery = new NewspaperDraftRecovery(Draft("local"), Draft("saved"), null, 8);
        system.Store(new NetEntity(1), 1, recovery);
        Assert.That(system.TryTake(new NetEntity(2), 1, out _), Is.False);
        Assert.That(system.TryTake(new NetEntity(1), 2, out _), Is.False);
        Assert.That(system.TryTake(new NetEntity(1), 1, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(recovery));
        Assert.That(system.TryTake(new NetEntity(1), 1, out _), Is.False);
    }

    [Test]
    public void DiscardRemovesRecoveryAndStorageRemainsBounded()
    {
        var system = new NewspaperDraftRecoverySystem();
        var recovery = new NewspaperDraftRecovery(Draft("local"), Draft("saved"), null, 8);
        system.Store(new NetEntity(1), 1, recovery);
        system.Store(new NetEntity(1), 1, null);
        Assert.That(system.TryTake(new NetEntity(1), 1, out _), Is.False);
        for (var i = 1; i <= 65; i++) system.Store(new NetEntity(i), 1, recovery);
        Assert.That(Enumerable.Range(1, 65).Count(i => system.TryTake(new NetEntity(i), 1, out _)), Is.EqualTo(64));
    }

    private static NewspaperEdition Draft(string text) => new() { PublicationId = 1, Name = "Edition", Blocks = new() { new() { Text = text } } };
}
