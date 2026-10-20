using System;
using System.Linq;
using Content.Shared._Forge.Newspapers;
using NUnit.Framework;

namespace Content.Tests._Forge.Newspapers;

[TestFixture]
public sealed class NewspaperStorageTests
{
    private static NewspaperEdition Article() => new()
    {
        Name = "News", PublicationId = 1,
        Blocks = [new NewspaperBlock { Text = "Article" }, new NewspaperBlock { Kind = NewspaperBlockKind.Photo, PhotoId = 7 }],
    };

    [Test]
    public void ArchiveRejectsDuplicatesFullCapacityAndCooldown()
    {
        var draft = Article();
        var published = draft.Clone(); published.Number = 1;
        var now = TimeSpan.FromSeconds(10);
        Assert.That(NewspaperStorage.CanPublish([published], draft, now, TimeSpan.Zero), Is.False);
        draft.Blocks[1].PhotoId = 8;
        Assert.That(NewspaperStorage.CanPublish([published], draft, now, TimeSpan.Zero), Is.True);
        Assert.That(NewspaperStorage.CanPublish([published], draft, now, now + TimeSpan.FromSeconds(2)), Is.False);
        Assert.That(NewspaperStorage.CanPublish(Enumerable.Repeat(published, NewspaperStorage.MaxEditions).ToArray(), draft, now, TimeSpan.Zero), Is.False);
    }

    [Test]
    public void SummariesExcludeArticleBodyAndPhotoReferences()
    {
        var edition = Article();
        edition.Blocks.Insert(0, new NewspaperBlock { Text = "Rubric", IsLabel = true });
        edition.Blocks[1].Text = new string('a', 4000);
        edition.Blocks.Add(new NewspaperBlock { Text = new string('b', 4000) });
        var summary = NewspaperStorage.Summary(edition);
        Assert.That(summary.Blocks.Single().Text.Length, Is.EqualTo(80));
        Assert.That(summary.PhotoIds, Is.Empty);
        Assert.That(edition.Blocks[1].Text.Length, Is.EqualTo(4000));
    }

    [Test]
    public void TotalPhotoBudgetIncludesPhotosOutsideImportBuffer()
    {
        var existing = new byte[1024 * 1024];
        var archive = Enumerable.Repeat(existing, 64).ToArray();
        Assert.That(NewspaperStorage.CanImport(archive, 1), Is.False);
        Assert.That(NewspaperStorage.CanImport(archive, 0), Is.True);
        Assert.That(NewspaperStorage.CanImport(archive.Take(63), existing.Length), Is.True);
    }

    [Test]
    public void UndoCannotRestoreForgottenPhotographsButKeepsAvailableOnes()
    {
        var history = Article(); history.PhotoId = 7;
        history.Blocks.Add(new NewspaperBlock { Kind = NewspaperBlockKind.Photo, PhotoId = 8 });
        history.Blocks.Add(new NewspaperBlock { Kind = NewspaperBlockKind.Photo, PhotoId = -2 });
        NewspaperStorage.RemoveUnavailablePhotos(history, [8]);
        Assert.That(history.PhotoIds.ToArray(), Is.EqualTo(new[] { 8 }));
        Assert.That(history.Blocks[0].Text, Is.EqualTo("Article"));
    }

    [Test]
    public void PhotoRemovalIsSafeToMergeButReplacingAPhotoIsAnEditConflict()
    {
        var previous = Article();
        var removed = previous.Clone(); removed.Blocks[1].PhotoId = -1;
        Assert.That(NewspaperStorage.IsPhotoRemoval(previous, removed, []), Is.True);
        removed.Blocks[1].PhotoId = 8;
        Assert.That(NewspaperStorage.IsPhotoRemoval(previous, removed, [7, 8]), Is.False);
        removed.Blocks[0].Text = "Concurrent text";
        Assert.That(NewspaperStorage.IsPhotoRemoval(previous, removed, []), Is.False);
    }
}
