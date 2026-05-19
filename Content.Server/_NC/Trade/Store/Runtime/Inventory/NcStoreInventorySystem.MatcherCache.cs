using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreInventorySystem
{
    private readonly InventoryMatcherService _matcherService = new();

    private sealed class InventoryMatcherService
    {
        public readonly Dictionary<string, CompiledMatcher?> CompiledItemGroupCache = new(StringComparer.Ordinal);
        public readonly Dictionary<string, CompiledMatcher?> CompiledMatcherCache = new(StringComparer.Ordinal);
        public readonly HashSet<string> OwnedCountedStackTypesScratch = new(StringComparer.Ordinal);

        public void Clear()
        {
            CompiledMatcherCache.Clear();
            CompiledItemGroupCache.Clear();
            OwnedCountedStackTypesScratch.Clear();
        }
    }

    private sealed class CompiledMatcher
    {
        public readonly HashSet<string> Items = new(StringComparer.Ordinal);
        public readonly HashSet<string> MatchStackTypes = new(StringComparer.Ordinal);
        public readonly List<string> Tags = new();
        public readonly Dictionary<string, bool> PrototypeTagMatchCache = new(StringComparer.Ordinal);

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

    private CompiledMatcher? GetCompiledMatcher(string matcherId, bool warnIfInvalid)
    {
        if (string.IsNullOrWhiteSpace(matcherId))
            return null;

        if (_matcherService.CompiledMatcherCache.TryGetValue(matcherId, out var cached))
            return cached;

        if (!_protos.TryIndex<NcMatcherPrototype>(matcherId, out var matcher))
        {
            if (warnIfInvalid)
                Sawmill.Warning($"[NcStore] matcher '{matcherId}' not found.");

            _matcherService.CompiledMatcherCache[matcherId] = null;
            return null;
        }

        var compiled = new CompiledMatcher(matcher);
        PrecomputeMatcherStackTypes(compiled);
        if (compiled.IsEmpty)
        {
            if (warnIfInvalid)
                Sawmill.Warning($"[NcStore] matcher '{matcherId}' has no items and no tags; request rejected.");

            _matcherService.CompiledMatcherCache[matcherId] = null;
            return null;
        }

        _matcherService.CompiledMatcherCache[matcherId] = compiled;
        return compiled;
    }

    private CompiledMatcher? GetCompiledItemGroupMatcher(NcItemGroupPrototype group)
    {
        if (string.IsNullOrWhiteSpace(group.ID))
            return null;

        if (_matcherService.CompiledItemGroupCache.TryGetValue(group.ID, out var cached))
            return cached;

        var compiled = new CompiledMatcher(group.Prototypes, group.Tags);
        PrecomputeMatcherStackTypes(compiled);
        if (compiled.IsEmpty)
        {
            _matcherService.CompiledItemGroupCache[group.ID] = null;
            return null;
        }

        _matcherService.CompiledItemGroupCache[group.ID] = compiled;
        return compiled;
    }

    private void PrecomputeMatcherStackTypes(CompiledMatcher matcher)
    {
        matcher.MatchStackTypes.Clear();
        foreach (var itemProtoId in matcher.Items)
        {
            var stackTypeId = GetProductStackType(itemProtoId);
            if (!string.IsNullOrWhiteSpace(stackTypeId))
                matcher.MatchStackTypes.Add(stackTypeId);
        }
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

        return matcher.MatchStackTypes.Contains(stackTypeId);
    }

    private int GetOwnedFromSnapshotForCompiledMatcher(in NcInventorySnapshot snapshot, CompiledMatcher matcher)
    {
        if (matcher.IsEmpty)
            return 0;

        var total = 0;
        var countedStackTypes = _matcherService.OwnedCountedStackTypesScratch;
        countedStackTypes.Clear();

        try
        {
            foreach (var stackTypeId in matcher.MatchStackTypes)
            {
                if (countedStackTypes.Add(stackTypeId) &&
                    snapshot.StackTypeCounts.TryGetValue(stackTypeId, out var stackCount))
                {
                    total += stackCount;
                }
            }

            foreach (var itemProtoId in matcher.Items)
            {
                var stackTypeId = GetProductStackType(itemProtoId);
                if (!string.IsNullOrWhiteSpace(stackTypeId))
                    continue;

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
        finally
        {
            countedStackTypes.Clear();
        }
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
        var matcher = GetCompiledItemGroupMatcher(group);
        return matcher == null ? 0 : GetOwnedFromSnapshotForCompiledMatcher(snapshot, matcher);
    }

    public bool TryTakeItemGroupUnitsFromRootCached(EntityUid root, NcItemGroupPrototype group, int amount)
    {
        if (amount <= 0)
            return true;

        var matcher = GetCompiledItemGroupMatcher(group);
        if (matcher == null)
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
