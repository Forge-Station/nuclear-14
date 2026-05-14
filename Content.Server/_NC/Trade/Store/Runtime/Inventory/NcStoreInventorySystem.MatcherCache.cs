using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreInventorySystem
{
    private sealed class CompiledMatcher
    {
        public readonly HashSet<string> Items = new(StringComparer.Ordinal);
        public readonly List<string> Tags = new();
        public readonly Dictionary<string, bool> PrototypeTagMatchCache = new(StringComparer.Ordinal);
        public readonly Dictionary<string, bool> StackTypeMatchCache = new(StringComparer.Ordinal);

        public bool IsEmpty => Items.Count == 0 && Tags.Count == 0;

        public CompiledMatcher(NcMatcherPrototype source)
            : this(source.Items, source.Tags) { }

        public CompiledMatcher(IReadOnlyList<string> items, IReadOnlyList<string> tags)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!string.IsNullOrWhiteSpace(item))
                    Items.Add(item);
            }

            var tagSet = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (!string.IsNullOrWhiteSpace(tag))
                    tagSet.Add(tag);
            }

            Tags.AddRange(tagSet);
            Tags.Sort(StringComparer.Ordinal);
        }
    }

    private readonly Dictionary<string, CompiledMatcher?> _compiledMatcherCache = new(StringComparer.Ordinal);

    private CompiledMatcher? GetCompiledMatcher(string matcherId, bool warnIfInvalid)
    {
        if (string.IsNullOrWhiteSpace(matcherId))
            return null;

        if (_compiledMatcherCache.TryGetValue(matcherId, out var cached))
            return cached;

        if (!_protos.TryIndex<NcMatcherPrototype>(matcherId, out var matcher))
        {
            if (warnIfInvalid)
                Sawmill.Warning($"[NcStore] matcher '{matcherId}' not found.");

            _compiledMatcherCache[matcherId] = null;
            return null;
        }

        var compiled = new CompiledMatcher(matcher);
        if (compiled.IsEmpty)
        {
            if (warnIfInvalid)
                Sawmill.Warning($"[NcStore] matcher '{matcherId}' has no items and no tags; request rejected.");

            _compiledMatcherCache[matcherId] = null;
            return null;
        }

        _compiledMatcherCache[matcherId] = compiled;
        return compiled;
    }

    private bool MatcherPrototypeHasAnyTag(CompiledMatcher matcher, string protoId)
    {
        if (matcher.Tags.Count == 0)
            return false;

        if (matcher.PrototypeTagMatchCache.TryGetValue(protoId, out var cached))
            return cached;

        var result = ProtoHasAnyMatcherTag(protoId, matcher.Tags);
        matcher.PrototypeTagMatchCache[protoId] = result;
        return result;
    }

    private bool MatcherMatchesStackType(CompiledMatcher matcher, string? stackTypeId)
    {
        if (string.IsNullOrWhiteSpace(stackTypeId))
            return false;

        if (matcher.StackTypeMatchCache.TryGetValue(stackTypeId, out var cached))
            return cached;

        var result = false;
        foreach (var itemProtoId in matcher.Items)
        {
            if (GetProductStackType(itemProtoId) == stackTypeId)
            {
                result = true;
                break;
            }
        }

        matcher.StackTypeMatchCache[stackTypeId] = result;
        return result;
    }

    private int GetOwnedFromSnapshotForCompiledMatcher(in NcInventorySnapshot snapshot, CompiledMatcher matcher)
    {
        if (matcher.IsEmpty)
            return 0;

        var total = 0;
        var countedStackTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var itemProtoId in matcher.Items)
        {
            var stackTypeId = GetProductStackType(itemProtoId);
            if (!string.IsNullOrWhiteSpace(stackTypeId))
            {
                if (countedStackTypes.Add(stackTypeId) &&
                    snapshot.StackTypeCounts.TryGetValue(stackTypeId, out var stackCount))
                {
                    total += stackCount;
                }

                continue;
            }

            if (snapshot.ProtoCounts.TryGetValue(itemProtoId, out var protoCount))
                total += protoCount;
        }

        if (matcher.Tags.Count == 0)
            return total;

        foreach (var (protoId, count) in snapshot.ProtoCounts)
        {
            if (count <= 0)
                continue;

            if (matcher.Items.Contains(protoId))
                continue;

            var stackTypeId = GetProductStackType(protoId);
            if (!string.IsNullOrWhiteSpace(stackTypeId) && countedStackTypes.Contains(stackTypeId))
                continue;

            if (!MatcherPrototypeHasAnyTag(matcher, protoId))
                continue;

            total += count;
        }

        return total;
    }

    public bool PrototypeMatchesMatcher(string matcherId, string protoId)
    {
        var matcher = GetCompiledMatcher(matcherId, warnIfInvalid: false);
        if (matcher == null)
            return false;

        if (matcher.Items.Contains(protoId))
            return true;

        return MatcherPrototypeHasAnyTag(matcher, protoId);
    }

    public void FillMatchingPrototypeIdsForMatcher(
        string matcherId,
        IReadOnlyDictionary<string, int> protoCounts,
        List<string> results)
    {
        results.Clear();

        var matcher = GetCompiledMatcher(matcherId, warnIfInvalid: false);
        if (matcher == null)
            return;

        foreach (var (protoId, count) in protoCounts)
        {
            if (count <= 0)
                continue;

            if (!matcher.Items.Contains(protoId) && !MatcherPrototypeHasAnyTag(matcher, protoId))
                continue;

            results.Add(protoId);
        }

        results.Sort(StringComparer.Ordinal);
    }

    public int GetOwnedFromSnapshotForItemGroup(in NcInventorySnapshot snapshot, NcItemGroupPrototype group)
    {
        var matcher = new CompiledMatcher(group.Prototypes, group.Tags);
        return GetOwnedFromSnapshotForCompiledMatcher(snapshot, matcher);
    }

        public bool TryTakeItemGroupUnitsFromRootCached(EntityUid root, NcItemGroupPrototype group, int amount)
        {
            if (amount <= 0)
                return true;

            var matcher = new CompiledMatcher(group.Prototypes, group.Tags);
            if (matcher.IsEmpty)
                return false;

            var request = new ProductTakeRequest(group.ID, null, PrototypeMatchMode.Matcher, matcher, true);
            var cachedItems = GetOrBuildDeepItemsCache(root);

            if (CalculateAvailableTakeUnits(root, cachedItems, request, amount) < amount)
                return false;

            var success = ExecuteTakeUnitsFromCachedItems(root, cachedItems, request, amount);
            if (success && _inventoryCache.TryGetValue(root, out var entry))
                MarkInventoryDirty(entry, ReferenceEquals(entry.Items, cachedItems));

            return success;
        }

}
