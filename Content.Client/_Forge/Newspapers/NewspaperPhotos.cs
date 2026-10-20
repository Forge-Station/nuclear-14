using System.Linq;
using Content.Client._Forge.Paper;
using Robust.Client.Graphics;
using Robust.Shared.Log;

namespace Content.Client._Forge.Newspapers;

/// <summary>Window-owned photo leases and failed downloads; textures live in the shared bounded cache.</summary>
internal sealed class NewspaperPhotos : IDisposable
{
    private readonly Func<string, byte[]?, DocumentResourceCache<DocumentTextures>.Lease?> _load;
    private readonly Action<string> _logError;
    private readonly Func<string, bool> _pending;
    private readonly Dictionary<int, (string Key, DocumentResourceCache<DocumentTextures>.Lease? Lease, bool Failed)> _entries = new();

    public NewspaperPhotos() : this(IoCManager.Resolve<IEntityManager>().System<DocumentTextureCacheSystem>().Photo,
        pending: IoCManager.Resolve<IEntityManager>().System<DocumentTextureCacheSystem>().PhotoPending) { }
    internal NewspaperPhotos(Func<string, byte[]?, DocumentResourceCache<DocumentTextures>.Lease?> load, Action<string>? logError = null,
        Func<string, bool>? pending = null)
    {
        _load = load;
        _logError = logError ?? (message => Logger.ErrorS("newspaper", message));
        _pending = pending ?? (_ => false);
    }

    // Suppress network retries while the decoder already owns the payload, including cache backpressure.
    public bool IsResolved(int id) => _entries.TryGetValue(id, out var entry) && (entry.Lease != null || entry.Failed || _pending(entry.Key));

    public Texture? Get(int id, string? key, byte[]? data = null)
    {
        if (id < 0 || string.IsNullOrEmpty(key)) return null;
        if (_entries.TryGetValue(id, out var entry))
        {
            if (entry.Key == key && (entry.Lease != null || entry.Failed)) return entry.Lease?.Value.Background;
            entry.Lease?.Dispose();
        }
        try
        {
            var lease = _load(key, data);
            // Null also means the bounded cache/decoder is busy. Only an invalid payload is permanent.
            _entries[id] = (key, lease, false);
            return lease?.Value.Background;
        }
        catch (Exception e)
        {
            _entries[id] = (key, null, true);
            _logError($"Cannot load newspaper photo: {e.Message}");
            return null;
        }
    }

    public void Retain(IReadOnlyDictionary<int, string> keys)
    {
        foreach (var id in _entries.Keys.Where(id => !keys.TryGetValue(id, out var key) || key != _entries[id].Key).ToArray())
        {
            _entries[id].Lease?.Dispose();
            _entries.Remove(id);
        }
    }

    public bool Poll()
    {
        foreach (var (id, entry) in _entries.ToArray())
            if (entry.Lease == null && !entry.Failed && Get(id, entry.Key) != null) return true;
        return false;
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Lease?.Dispose();
        _entries.Clear();
    }
}
