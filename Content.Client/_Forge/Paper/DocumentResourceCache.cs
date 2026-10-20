using System.Linq;

namespace Content.Client._Forge.Paper;

/// <summary>Bounded resources with leases. Closing a window only releases a lease, not GPU resources.</summary>
internal sealed class DocumentResourceCache<T> : IDisposable where T : class, IDisposable
{
    private sealed class Entry(T value, long bytes)
    {
        public readonly T Value = value;
        public readonly long Bytes = bytes;
        public int Users;
        public long Used;
    }
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly long _budget;
    private long _clock;
    public long Bytes { get; private set; }
    public DocumentResourceCache(long budget) => _budget = budget;
    public sealed class Lease(T value, Action release) : IDisposable
    {
        private Action? _release = release;
        public readonly T Value = value;
        public void Dispose() { _release?.Invoke(); _release = null; }
    }
    public Lease? Acquire(string key, long bytes, Func<T> load)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            if (bytes <= 0 || bytes > _budget) return null;
            while (Bytes + bytes > _budget)
            {
                var oldest = _entries.Where(p => p.Value.Users == 0).OrderBy(p => p.Value.Used).FirstOrDefault();
                if (oldest.Value == null) return null; // Never evict a texture still drawn by another window.
                _entries.Remove(oldest.Key); Bytes -= oldest.Value.Bytes; oldest.Value.Value.Dispose();
            }
            entry = new Entry(load(), bytes);
            _entries.Add(key, entry); Bytes += bytes;
        }
        return Acquire(entry);
    }
    public Lease? AcquireExisting(string key) => _entries.TryGetValue(key, out var entry) ? Acquire(entry) : null;

    private Lease Acquire(Entry entry)
    {
        entry.Users++; entry.Used = ++_clock;
        return new Lease(entry.Value, () => { entry.Users--; entry.Used = ++_clock; });
    }
    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Value.Dispose();
        _entries.Clear(); Bytes = 0;
    }
}
