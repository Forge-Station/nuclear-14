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

        if (!_prototypes.TryIndex<NcMatcherPrototype>(matcherId, out var matcher))
        {
            Sawmill.Warning($"[Contracts] Matcher '{matcherId}' not found.");
            _contractMatcherCache[matcherId] = null;
            return false;
        }

        var matchItems = new HashSet<string>(StringComparer.Ordinal);
        var spawnPool = new List<string>();
        for (var i = 0; i < matcher.Items.Count; i++)
        {
            var itemId = matcher.Items[i];
            if (string.IsNullOrWhiteSpace(itemId))
                continue;

            matchItems.Add(itemId);
            if (_prototypes.HasIndex<EntityPrototype>(itemId))
                spawnPool.Add(itemId);
        }

        var matchTags = new List<string>();
        for (var i = 0; i < matcher.Tags.Count; i++)
        {
            var tag = matcher.Tags[i];
            if (!string.IsNullOrWhiteSpace(tag))
                matchTags.Add(tag);
        }

        if (matchItems.Count == 0 && matchTags.Count == 0)
        {
            Sawmill.Warning($"[Contracts] Matcher '{matcherId}' has no items and no tags.");
            _contractMatcherCache[matcherId] = null;
            return false;
        }

        var resolved = new ContractMatcherSpec(matchItems, spawnPool, matchTags);
        _contractMatcherCache[matcherId] = resolved;
        spec = resolved;
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

