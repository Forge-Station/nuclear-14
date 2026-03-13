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

        private static bool SparseDictEqualsWithPreserve(
            Dictionary<string, int> src,
            Dictionary<string, int> dst,
            HashSet<string> preserveMissingIds
        )
        {
            foreach (var (k, v) in src)
            {
                if (!dst.TryGetValue(k, out var other) || other != v)
                    return false;
            }

            foreach (var key in dst.Keys)
            {
                if (src.ContainsKey(key))
                    continue;

                if (!preserveMissingIds.Contains(key))
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

        private static void ApplySparseSnapshotWithPreserve(
            Dictionary<string, int> src,
            Dictionary<string, int> dst,
            HashSet<string> preserveMissingIds
        )
        {
            var toRemove = new List<string>();

            foreach (var key in dst.Keys)
            {
                if (src.ContainsKey(key))
                    continue;

                if (!preserveMissingIds.Contains(key))
                    toRemove.Add(key);
            }

            for (var i = 0; i < toRemove.Count; i++)
                dst.Remove(toRemove[i]);

            foreach (var (k, v) in src)
            {
                if (string.IsNullOrWhiteSpace(k))
                    continue;

                dst[k] = v;
            }
        }
    }
}
