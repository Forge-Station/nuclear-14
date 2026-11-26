using System.Collections.Generic;
using Content.Shared._Forge.Geometry;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._Forge.Geometry;

[TestFixture]
public sealed class GridLineTraversalTest
{
    [Test]
    public void CrossedCellsAreVisitedInOrder()
    {
        var cells = new List<Vector2i>();
        var completed = GridLineTraversal.Visit(new Vector2i(0, 0), new Vector2i(3, 1), cell =>
        {
            cells.Add(cell);
            return true;
        });

        Assert.That(completed, Is.True);
        Assert.That(cells, Is.EqualTo(new[] { new Vector2i(1, 0), new Vector2i(2, 1), new Vector2i(3, 1) }));
    }

    [Test]
    public void BlockerStopsTraversal()
    {
        var cells = new List<Vector2i>();
        var completed = GridLineTraversal.Visit(new Vector2i(0, 0), new Vector2i(4, 0), cell =>
        {
            cells.Add(cell);
            return cell.X != 2;
        });

        Assert.That(completed, Is.False);
        Assert.That(cells, Is.EqualTo(new[] { new Vector2i(1, 0), new Vector2i(2, 0) }));
    }
}
