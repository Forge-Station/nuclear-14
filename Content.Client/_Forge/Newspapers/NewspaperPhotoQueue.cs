using System.Linq;

namespace Content.Client._Forge.Newspapers;

/// <summary>Requests missing photographs gradually, with bounded retries per photograph.</summary>
internal sealed class NewspaperPhotoQueue
{
    private readonly Dictionary<int, NewspaperPhotoRequest> _requests = new();
    private TimeSpan _nextRequest;
    private int _lastRequest = -1;

    public bool TryRequest(IEnumerable<int> ids, int edition, TimeSpan now, Func<int, bool> resolved, out int id)
    {
        id = -1;
        var wanted = ids.Where(i => i >= 0).Distinct().ToArray();
        foreach (var stale in _requests.Keys.Where(i => !wanted.Contains(i)).ToArray()) _requests.Remove(stale);
        if (now < _nextRequest) return false;
        var start = Array.IndexOf(wanted, _lastRequest) + 1;
        foreach (var candidate in wanted.Skip(start).Concat(wanted.Take(start)))
        {
            if (!_requests.TryGetValue(candidate, out var request))
                _requests[candidate] = request = new NewspaperPhotoRequest();
            request.SetTarget(candidate, edition);
            if (!request.TryRequest(now, resolved(candidate), out _)) continue;
            id = candidate;
            _lastRequest = candidate;
            _nextRequest = now + TimeSpan.FromMilliseconds(500);
            return true;
        }
        return false;
    }
}
