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

        private static bool SparseDictEqualsPreservingHiddenBuyListings(
            Dictionary<string, int> authoritativeValues,
            Dictionary<string, int> cachedValues,
            HashSet<string> buyListingIds
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

                if (!buyListingIds.Contains(key))
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

        private static void ApplySparseSnapshotPreservingHiddenBuyListings(
            Dictionary<string, int> authoritativeValues,
            Dictionary<string, int> cachedValues,
            HashSet<string> buyListingIds
        )
        {
            var toRemove = new List<string>();

            foreach (var key in cachedValues.Keys)
            {
                if (authoritativeValues.ContainsKey(key))
                    continue;

                if (!buyListingIds.Contains(key))
                    toRemove.Add(key);
            }

            for (var i = 0; i < toRemove.Count; i++)
                cachedValues.Remove(toRemove[i]);

            foreach (var (key, value) in authoritativeValues)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                cachedValues[key] = value;
            }
        }
    }
}
