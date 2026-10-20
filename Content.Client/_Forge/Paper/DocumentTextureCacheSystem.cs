using Robust.Client.Graphics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Content.Shared._Forge.Paper;
using Content.Shared._Forge.Photo;
using Robust.Shared.Timing;
using System.Linq;
using Content.Shared.GameTicking;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Paper;

internal sealed class DocumentTextures(Texture background, Texture? overlay = null) : IDisposable
{
    public readonly Texture Background = background;
    public readonly Texture? Overlay = overlay;
    public void Dispose() { (Background as IDisposable)?.Dispose(); (Overlay as IDisposable)?.Dispose(); }
}

public sealed class DocumentTextureCacheSystem : EntitySystem
{
    internal readonly DocumentResourceCache<DocumentTextures> Cache = new(64L * 1024 * 1024);
    private sealed class PendingSurface(Task<(Image<Rgba32> Background, Image<Rgba32> Overlay)> task, TimeSpan lastUsed)
    {
        public readonly Task<(Image<Rgba32> Background, Image<Rgba32> Overlay)> Task = task;
        public TimeSpan LastUsed = lastUsed;
    }
    private readonly Dictionary<string, PendingSurface> _pending = new();
    private sealed class PendingPhoto(Task<Image<Rgba32>> task, TimeSpan lastUsed)
    {
        public readonly Task<Image<Rgba32>> Task = task;
        public TimeSpan LastUsed = lastUsed;
    }
    private readonly Dictionary<string, PendingPhoto> _photos = new();
    private int _activeWorkers;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextSweep;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.RealTime < _nextSweep) return;
        _nextSweep = _timing.RealTime + TimeSpan.FromSeconds(1);
        foreach (var key in _pending.Where(p => _timing.RealTime - p.Value.LastUsed > TimeSpan.FromSeconds(2)).Select(p => p.Key).ToArray())
        {
            _ = DisposeWhenReady(_pending[key].Task); _pending.Remove(key);
        }
        foreach (var key in _photos.Where(p => _timing.RealTime - p.Value.LastUsed > TimeSpan.FromSeconds(2)).Select(p => p.Key).ToArray())
        {
            _ = DisposePhotoWhenReady(_photos[key].Task); _photos.Remove(key);
        }
    }
    private static async Task DisposePhotoWhenReady(Task<Image<Rgba32>> task)
    {
        try { (await task).Dispose(); }
        catch { /* Failed decoding has no image to release. */ }
    }
    private static async Task DisposeWhenReady(Task<(Image<Rgba32> Background, Image<Rgba32> Overlay)> task)
    {
        try
        {
            var images = await task;
            images.Background.Dispose(); images.Overlay.Dispose();
        }
        catch { /* A failed generation has no image buffers to release. */ }
    }
    internal DocumentResourceCache<DocumentTextures>.Lease? Surface(string key, long bytes,
        PaperSurfaceAppearance appearance, PaperSurfacePrototype profile, int width, int height, int side)
    {
        var cached = Cache.AcquireExisting(key);
        if (cached != null) return cached;
        if (!_pending.TryGetValue(key, out var pending))
        {
            if (_pending.Count >= 4 || Interlocked.CompareExchange(ref _activeWorkers, 0, 0) >= 2) return null;
            var snapshot = appearance.Clone();
            Interlocked.Increment(ref _activeWorkers);
            _pending[key] = new PendingSurface(Task.Run(() =>
            {
                try { return PaperSurfaceGenerator.Generate(snapshot, profile, width, height, side); }
                finally { Interlocked.Decrement(ref _activeWorkers); }
            }), _timing.RealTime);
            return null;
        }
        pending.LastUsed = _timing.RealTime;
        var task = pending.Task;
        if (!task.IsCompleted) return null;
        if (!task.IsCompletedSuccessfully) { _pending.Remove(key); return null; }
        // IsCompletedSuccessfully above guarantees this read never waits on the UI thread.
#pragma warning disable RA0004
        var images = task.Result;
#pragma warning restore RA0004
        // Texture upload is done on the UI thread; generation contains no graphics calls.
        var lease = Cache.Acquire(key, bytes, () =>
        {
            var background = Texture.LoadFromImage(images.Background, "cached paper background");
            try { return new DocumentTextures(background, Texture.LoadFromImage(images.Overlay, "cached paper creases")); }
            catch { (background as IDisposable)?.Dispose(); throw; }
        });
        if (lease != null)
        {
            _pending.Remove(key); images.Background.Dispose(); images.Overlay.Dispose();
        }
        return lease;
    }
    private void Clear()
    {
        foreach (var pending in _pending.Values) _ = DisposeWhenReady(pending.Task);
        foreach (var pending in _photos.Values) _ = DisposePhotoWhenReady(pending.Task);
        _pending.Clear(); _photos.Clear(); Cache.Dispose();
    }
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
    }
    public override void Shutdown() { Clear(); base.Shutdown(); }
    internal bool PhotoPending(string key) => _photos.ContainsKey(key);
    internal DocumentResourceCache<DocumentTextures>.Lease? Photo(string key, byte[]? data = null)
    {
        var cacheKey = "photo:" + key;
        var existing = Cache.AcquireExisting(cacheKey);
        if (existing != null) return existing;
        if (!_photos.TryGetValue(key, out var pending))
        {
            if (data == null) return null;
            if (data.Length > 4 * 1024 * 1024 || !TryPhotoSize(data, out _, out _))
                throw new InvalidDataException("Invalid or oversized photograph PNG.");
            if (_photos.Count >= 4 || Interlocked.CompareExchange(ref _activeWorkers, 0, 0) >= 2) return null;
            Interlocked.Increment(ref _activeWorkers);
            _photos[key] = new PendingPhoto(Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(data, false);
                    return Image.Load<Rgba32>(stream);
                }
                finally { Interlocked.Decrement(ref _activeWorkers); }
            }), _timing.RealTime);
            return null;
        }
        pending.LastUsed = _timing.RealTime;
        if (!pending.Task.IsCompleted) return null;
        if (!pending.Task.IsCompletedSuccessfully)
        {
            _photos.Remove(key);
            _ = DisposePhotoWhenReady(pending.Task);
            throw new InvalidDataException("Cannot decode photograph PNG.");
        }
#pragma warning disable RA0004
        var image = pending.Task.Result;
#pragma warning restore RA0004
        // Only the graphics upload runs on the UI thread. A full cache is retryable.
        var lease = Cache.Acquire(cacheKey, (long)image.Width * image.Height * 4,
            () => new DocumentTextures(Texture.LoadFromImage(image, "cached document photograph")));
        if (lease != null) { _photos.Remove(key); image.Dispose(); }
        return lease;
    }

    internal static bool TryPhotoSize(byte[] data, out int width, out int height) =>
        PngUtility.TryGetSize(data, out width, out height) && width <= 2048 && height <= 2048;
}
