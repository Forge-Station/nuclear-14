namespace Content.Client._NC.Trade;

public sealed partial class NcStoreMenu
{
    private sealed partial class UiStateBinder
    {
        private static bool DictEquals(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            foreach (var (k, v) in a)
            {
                if (!b.TryGetValue(k, out var other) || other != v)
                    return false;
            }

            return true;
        }

        private static bool ScopedDictEquals(
            Dictionary<string, int> authoritativeValues,
            Dictionary<string, int> cachedValues,
            HashSet<string> snapshotScopeIds
        )
        {
            foreach (var (key, value) in authoritativeValues)
            {
                if (!cachedValues.TryGetValue(key, out var other) || other != value)
                    return false;
            }

            foreach (var key in cachedValues.Keys)
            {
                if (authoritativeValues.ContainsKey(key))
                    continue;

                if (snapshotScopeIds.Contains(key))
                    return false;
            }

            return true;
        }

        private static void ApplySparseSnapshot(Dictionary<string, int> src, Dictionary<string, int> dst)
        {
            dst.Clear();

            foreach (var (k, v) in src)
            {
                if (string.IsNullOrWhiteSpace(k))
                    continue;

                dst[k] = v;
            }
        }

        private void ApplyScopedSnapshot(
            Dictionary<string, int> authoritativeValues,
            Dictionary<string, int> cachedValues,
            HashSet<string> snapshotScopeIds
        )
        {
            _scopedRemoveScratch.Clear();

            foreach (var key in cachedValues.Keys)
            {
                if (authoritativeValues.ContainsKey(key))
                    continue;

                if (snapshotScopeIds.Contains(key))
                    _scopedRemoveScratch.Add(key);
            }

            for (var i = 0; i < _scopedRemoveScratch.Count; i++)
                cachedValues.Remove(_scopedRemoveScratch[i]);

            _scopedRemoveScratch.Clear();

            foreach (var (key, value) in authoritativeValues)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                cachedValues[key] = value;
            }
        }
    }
}
