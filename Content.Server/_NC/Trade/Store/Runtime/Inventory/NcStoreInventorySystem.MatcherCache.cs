using Content.Shared._NC.Trade;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreInventorySystem
{
    private readonly InventoryMatcherService _matcherService = new();

    private sealed class InventoryMatcherService
    {
        public readonly Dictionary<string, CompiledMatcher?> CompiledItemGroupCache = new(StringComparer.Ordinal);
        public readonly Dictionary<string, CompiledMatcher?> CompiledMatcherCache = new(StringComparer.Ordinal);
        public readonly HashSet<string> OwnedCountedStackTypesScratch = new(StringComparer.Ordinal);
        public readonly Dictionary<(string ProtoId, string TagId), bool> PrototypeTagMatchCache = new();

        public void Clear()
        {
            CompiledMatcherCache.Clear();
            CompiledItemGroupCache.Clear();
            OwnedCountedStackTypesScratch.Clear();
            PrototypeTagMatchCache.Clear();
        }
    }

    private sealed class CompiledMatcher
    {
        public readonly HashSet<string> Items = new(StringComparer.Ordinal);
        public readonly HashSet<string> MatchStackTypes = new(StringComparer.Ordinal);

        public bool IsEmpty => Items.Count == 0;

        public CompiledMatcher(NcMatcherPrototype source)
            : this(source.Items) { }

        public CompiledMatcher(IReadOnlyList<string> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!string.IsNullOrWhiteSpace(item))
                    Items.Add(item);
            }
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
                Sawmill.Warning($"[NcStore] matcher '{matcherId}' has no items; request rejected.");

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

        var compiled = new CompiledMatcher(group.Prototypes);
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

        return false;
    }

    public bool PrototypeHasTag(string protoId, string tagId)
    {
        if (string.IsNullOrWhiteSpace(protoId) || string.IsNullOrWhiteSpace(tagId))
            return false;

        var key = (protoId, tagId);
        if (_matcherService.PrototypeTagMatchCache.TryGetValue(key, out var cached))
            return cached;

        var result = PrototypeHasTagUncached(protoId, tagId);
        _matcherService.PrototypeTagMatchCache[key] = result;
        return result;
    }

    private bool PrototypeHasTagUncached(string protoId, string tagId)
    {
        if (!_protos.TryIndex<EntityPrototype>(protoId, out var proto))
            return false;

        if (!proto.TryGetComponent(out TagComponent? tagComponent, _compFactory) || tagComponent == null)
            return false;

        return _tags.HasTag(tagComponent, tagId);
    }

    private bool TryResolveTradeTagId(string tagTargetId, out string tagId)
    {
        tagId = string.Empty;

        if (string.IsNullOrWhiteSpace(tagTargetId))
            return false;

        if (!_protos.TryIndex<NcTradeTagPrototype>(tagTargetId, out var tagTarget))
            return false;

        if (string.IsNullOrWhiteSpace(tagTarget.Tag) || !_protos.HasIndex<TagPrototype>(tagTarget.Tag))
            return false;

        tagId = tagTarget.Tag;
        return true;
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

            if (!matcher.Items.Contains(protoId))
                continue;

            results.Add(protoId);
        }

        results.Sort(StringComparer.Ordinal);
    }

    public void FillMatchingStackTypeIdsForMatcher(
        string matcherId,
        IReadOnlyDictionary<string, int> stackTypeCounts,
        List<string> results)
    {
        results.Clear();

        var matcher = GetCompiledMatcher(matcherId, warnIfInvalid: false);
        if (matcher == null || matcher.MatchStackTypes.Count == 0)
            return;

        foreach (var (stackTypeId, count) in stackTypeCounts)
        {
            if (count <= 0 || !matcher.MatchStackTypes.Contains(stackTypeId))
                continue;

            results.Add(stackTypeId);
        }

        results.Sort(StringComparer.Ordinal);
    }

    public void FillMatchingPrototypeIdsForTag(
        string tagTargetId,
        IReadOnlyDictionary<string, int> protoCounts,
        List<string> results)
    {
        results.Clear();

        if (!TryResolveTradeTagId(tagTargetId, out var tagId))
            return;

        foreach (var (protoId, count) in protoCounts)
        {
            if (count <= 0 || !PrototypeHasTag(protoId, tagId))
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
