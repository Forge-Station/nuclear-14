using System;
using Content.Client._Forge.Paper;
using NUnit.Framework;
namespace Content.Tests._Forge.Paper;
[TestFixture]
public sealed class DocumentResourceCacheTests
{
    private sealed class Resource : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }
    [Test]
    public void ReopeningReusesResourceAndClosingDoesNotDestroyIt()
    {
        using var cache = new DocumentResourceCache<Resource>(100);
        var loads = 0; var resource = new Resource();
        var first = cache.Acquire("sheet", 40, () => { loads++; return resource; })!;
        first.Dispose(); first.Dispose();
        var reopened = cache.Acquire("sheet", 40, () => { loads++; return new Resource(); })!;
        Assert.That(reopened.Value, Is.SameAs(resource));
        Assert.That(loads, Is.EqualTo(1));
        Assert.That(resource.Disposals, Is.Zero);
        reopened.Dispose();
    }
    [Test]
    public void MemoryBudgetCannotEvictAnOpenDocument()
    {
        using var cache = new DocumentResourceCache<Resource>(100);
        using var open = cache.Acquire("open", 80, () => new Resource())!;
        var loads = 0;
        var rejected = cache.Acquire("another", 80, () => { loads++; return new Resource(); });
        Assert.That(rejected, Is.Null);
        Assert.That(loads, Is.Zero);
        Assert.That(open.Value.Disposals, Is.Zero);
        Assert.That(cache.Bytes, Is.EqualTo(80));
    }
    [Test]
    public void IdleResourcesAreEvictedWithinBudget()
    {
        using var cache = new DocumentResourceCache<Resource>(100);
        var old = cache.Acquire("old", 80, () => new Resource())!;
        var resource = old.Value; old.Dispose();
        using var next = cache.Acquire("new", 80, () => new Resource())!;
        Assert.That(resource.Disposals, Is.EqualTo(1));
        Assert.That(cache.Bytes, Is.EqualTo(80));
    }
    [Test]
    public void RoundCleanupDisposesResourcesOnce()
    {
        var cache = new DocumentResourceCache<Resource>(100);
        var lease = cache.Acquire("sheet", 80, () => new Resource())!;
        var resource = lease.Value;
        cache.Dispose(); lease.Dispose(); cache.Dispose();
        Assert.That(resource.Disposals, Is.EqualTo(1));
        Assert.That(cache.Bytes, Is.Zero);
    }
}
