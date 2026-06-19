using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private readonly Dictionary<string, ContractMatcherSpec?> _contractMatcherCache = new(StringComparer.Ordinal);

    private bool TryGetStackTypeId(string productProtoId, out string stackTypeId)
    {
        stackTypeId = string.Empty;

        if (!_prototypes.TryIndex<EntityPrototype>(productProtoId, out var expectedProto))
            return false;

        var stackComponentName = _compFactory.GetComponentName(typeof(StackComponent));
        if (!expectedProto.TryGetComponent(stackComponentName, out StackComponent? prodStackDef))
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
            BuildContractMatcherSpecFromLists(matcher.Items, out var matcherSpec);
            if (!CacheContractMatcherSpec(matcherId, matcherSpec, "Matcher"))
                return false;

            spec = matcherSpec;
            return true;
        }

        if (_prototypes.TryIndex<NcItemGroupPrototype>(matcherId, out var group))
        {
            BuildContractMatcherSpecFromLists(group.Prototypes, out var groupSpec);
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
        out ContractMatcherSpec spec
    )
    {
        var matchItems = new HashSet<string>(StringComparer.Ordinal);
        var matchStackTypes = new HashSet<string>(StringComparer.Ordinal);
        var spawnPool = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var itemId = items[i];
            if (string.IsNullOrWhiteSpace(itemId))
                continue;

            matchItems.Add(itemId);

            // Stack prototypes often have several entity variants for the same logical item
            // (for example x1/x10/x30 stack prototypes). A group that contains the x1
            // prototype should still match a larger stack with the same StackTypeId.
            if (TryGetStackTypeId(itemId, out var stackTypeId))
                matchStackTypes.Add(stackTypeId);

            if (_prototypes.HasIndex<EntityPrototype>(itemId))
                spawnPool.Add(itemId);
        }

        spec = new(matchItems, matchStackTypes, spawnPool);
    }

    private bool CacheContractMatcherSpec(string matcherId, ContractMatcherSpec spec, string sourceKind)
    {
        if (spec.MatchItems.Count == 0)
        {
            Sawmill.Warning($"[Contracts] {sourceKind} '{matcherId}' has no prototypes/items.");
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

    private bool ContractPrototypeHasTag(string protoId, string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return false;

        if (!_prototypes.TryIndex<EntityPrototype>(protoId, out var proto))
            return false;

        if (!proto.TryGetComponent(out TagComponent? tagComponent, _compFactory) || tagComponent == null)
            return false;

        return _tags.HasTag(tagComponent, tagId);
    }

    private bool TryResolveContractTagTargetId(string tagTargetId, out string tagId)
    {
        tagId = string.Empty;

        if (string.IsNullOrWhiteSpace(tagTargetId))
            return false;

        if (!_prototypes.TryIndex<NcTradeTagPrototype>(tagTargetId, out var tagTarget))
            return false;

        if (string.IsNullOrWhiteSpace(tagTarget.Tag) || !_prototypes.HasIndex<TagPrototype>(tagTarget.Tag))
            return false;

        tagId = tagTarget.Tag;
        return true;
    }

    private sealed class ContractMatcherSpec
    {
        public readonly HashSet<string> MatchItems;
        public readonly HashSet<string> MatchStackTypes;
        public readonly List<string> SpawnPool;

        public ContractMatcherSpec(
            HashSet<string> matchItems,
            HashSet<string> matchStackTypes,
            List<string> spawnPool
        )
        {
            MatchItems = matchItems;
            MatchStackTypes = matchStackTypes;
            SpawnPool = spawnPool;
        }
    }
}
