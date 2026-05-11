using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private sealed class ContractMatcherSpec
    {
        public readonly HashSet<string> MatchItems;
        public readonly List<string> SpawnPool;
        public readonly List<string> MatchTags;

        public ContractMatcherSpec(
            HashSet<string> matchItems,
            List<string> spawnPool,
            List<string> matchTags)
        {
            MatchItems = matchItems;
            SpawnPool = spawnPool;
            MatchTags = matchTags;
        }
    }

    private readonly Dictionary<string, ContractMatcherSpec?> _contractMatcherCache = new(StringComparer.Ordinal);

    private bool TryGetStackTypeId(string productProtoId, out string stackTypeId)
    {
        stackTypeId = string.Empty;

        if (!_prototypes.TryIndex<EntityPrototype>(productProtoId, out var expectedProto))
            return false;

        if (!expectedProto.TryGetComponent("Stack", out StackComponent? prodStackDef))
            return false;

        if (string.IsNullOrWhiteSpace(prodStackDef.StackTypeId))
            return false;

        stackTypeId = prodStackDef.StackTypeId;
        return true;
    }

    private bool TryGetContractMatcherSpec(string matcherId, out ContractMatcherSpec spec)
    {
        spec = default!;

        if (string.IsNullOrWhiteSpace(matcherId))
            return false;

        if (_contractMatcherCache.TryGetValue(matcherId, out var cached))
        {
            if (cached == null)
                return false;

            spec = cached;
            return true;
        }

        if (_prototypes.TryIndex<NcMatcherPrototype>(matcherId, out var matcher))
        {
            BuildContractMatcherSpecFromLists(matcher.Items, matcher.Tags, out var matcherSpec);
            if (!CacheContractMatcherSpec(matcherId, matcherSpec, "Matcher"))
                return false;

            spec = matcherSpec;
            return true;
        }

        if (_prototypes.TryIndex<NcItemGroupPrototype>(matcherId, out var group))
        {
            BuildContractMatcherSpecFromLists(group.Prototypes, group.Tags, out var groupSpec);
            if (!CacheContractMatcherSpec(matcherId, groupSpec, "Item group"))
                return false;

            spec = groupSpec;
            return true;
        }

        Sawmill.Warning($"[Contracts] Matcher/item group '{matcherId}' not found.");
        _contractMatcherCache[matcherId] = null;
        return false;
    }

    private void BuildContractMatcherSpecFromLists(
        IReadOnlyList<string> items,
        IReadOnlyList<string> tags,
        out ContractMatcherSpec spec)
    {
        var matchItems = new HashSet<string>(StringComparer.Ordinal);
        var spawnPool = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var itemId = items[i];
            if (string.IsNullOrWhiteSpace(itemId))
                continue;

            matchItems.Add(itemId);
            if (_prototypes.HasIndex<EntityPrototype>(itemId))
                spawnPool.Add(itemId);
        }

        var matchTags = new List<string>();
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (!string.IsNullOrWhiteSpace(tag))
                matchTags.Add(tag);
        }

        spec = new ContractMatcherSpec(matchItems, spawnPool, matchTags);
    }

    private bool CacheContractMatcherSpec(string matcherId, ContractMatcherSpec spec, string sourceKind)
    {
        if (spec.MatchItems.Count == 0 && spec.MatchTags.Count == 0)
        {
            Sawmill.Warning($"[Contracts] {sourceKind} '{matcherId}' has no prototypes/items and no tags.");
            _contractMatcherCache[matcherId] = null;
            return false;
        }

        _contractMatcherCache[matcherId] = spec;
        return true;
    }

    private bool TryPickMatcherSpawnPrototype(string matcherId, out string prototypeId)
    {
        prototypeId = string.Empty;

        if (!TryGetContractMatcherSpec(matcherId, out var spec))
            return false;

        if (spec.SpawnPool.Count == 0)
            return false;

        prototypeId = _random.Pick(spec.SpawnPool);
        return true;
    }

}

