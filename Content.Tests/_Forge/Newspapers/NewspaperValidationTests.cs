using System;
using System.Linq;
using Content.Shared._Forge.Newspapers;
using NUnit.Framework;

namespace Content.Tests._Forge.Newspapers;

[TestFixture]
public sealed class NewspaperValidationTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    public void RejectsOutOfBoundsGeometryAndExcessiveContent(int invalidCase)
    {
        var edition = new NewspaperEdition { Blocks = [new NewspaperBlock()] };
        Assert.That(NewspaperLayout.IsValid(edition), Is.True);
        switch (invalidCase)
        {
            case 0: edition.Width = NewspaperLayout.MinPageSize - 1; break;
            case 1: edition.Height = NewspaperLayout.MaxPageSize + 1; break;
            case 2: edition.Pages = 3; break;
            case 3:
                edition.Blocks = Enumerable.Range(0, NewspaperLayout.MaxBlocks + 1).Select(_ => new NewspaperBlock()).ToList();
                break;
            case 4: edition.Blocks[0].X = int.MaxValue; break;
            case 5: edition.Blocks[0].Font = (NewspaperFont)255; break;
            case 6: edition.Blocks[0].Text = new string('x', NewspaperLayout.MaxTextLength + 1); break;
            case 7:
                edition.Blocks = Enumerable.Range(0, 5).Select(_ => new NewspaperBlock
                    { Text = new string('x', NewspaperLayout.MaxTotalText / 5 + 1) }).ToList();
                break;
        }
        Assert.That(NewspaperLayout.IsValid(edition), Is.False);
    }

    [Test]
    public void PublishedSnapshotKeepsItsTextGeometryAndPhotographAfterDraftEdits()
    {
        var draft = new NewspaperEdition
        {
            Name = "News", PublicationId = 1, PhotoId = 7,
            Blocks = [new NewspaperBlock { Text = "Original" }, new NewspaperBlock { Kind = NewspaperBlockKind.Photo }],
        };
        var published = draft.Clone();
        draft.Blocks[0].Text = "Changed";
        draft.Blocks[0].X = 100;
        draft.Blocks.RemoveAt(1);
        draft.PhotoId = 8;
        Assert.That(published.Blocks[0].Text, Is.EqualTo("Original"));
        Assert.That(published.Blocks[0].X, Is.Zero);
        Assert.That(published.Blocks, Has.Count.EqualTo(2));
        Assert.That(published.PhotoIds.Single(), Is.EqualTo(7));
    }
}
