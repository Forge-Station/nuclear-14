using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private List<ContractRewardData> BakeRewardsForContract(
        EntityUid store,
        string contractProtoId,
        List<ContractRewardDef> rewards
    )
    {
        if (rewards.Count == 0)
            return new();

        var baked = BakeRewardsRecursive(store, contractProtoId, rewards, 0);
        return AggregateRewards(baked);
    }

    private List<ContractRewardData> BakeRewardsRecursive(
        EntityUid store,
        string contractProtoId,
        List<ContractRewardDef> blueprints,
        int depth
    )
    {
        var result = new List<ContractRewardData>();
        if (depth > MaxRewardDepth)
            return result;

        for (var i = 0; i < blueprints.Count; i++)
        {
            var bp = blueprints[i];
            var rewardId = GetRewardId(bp);
            var count = RollSoft(
                new(QuasiKeyKind.RAmount, store, contractProtoId, $"{depth}:{i}:{bp.Type}:{rewardId}"),
                bp.Count,
                0);

            if (count <= 0)
                continue;

            if (bp.Type == StoreRewardType.Pool)
            {
                var rolled = RollPool(store, contractProtoId, bp, count, depth + 1);
                result.AddRange(rolled);
                continue;
            }

            if (string.IsNullOrWhiteSpace(rewardId))
                continue;

            if (bp.Type != StoreRewardType.Item && bp.Type != StoreRewardType.Currency)
                continue;

            result.Add(new(bp.Type, rewardId, count));
        }

        return result;
    }

    private List<ContractRewardData> RollPool(
        EntityUid store,
        string contractProtoId,
        ContractRewardDef poolDef,
        int rolls,
        int depth
    )
    {
        var output = new List<ContractRewardData>();
        if (depth > MaxRewardDepth)
            return output;

        if (!TryResolveRewardPoolOptions(poolDef, out var options))
            return output;

        var deck = CreateRewardPoolDeck(options);
        var dropCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < rolls; i++)
            if (!TryRollRewardPoolEntry(store, contractProtoId, deck, dropCounts, depth, output))
                break;

        return output;
    }

    private bool TryResolveRewardPoolOptions(ContractRewardDef poolDef, out List<ContractRewardDef> options)
    {
        var poolId = GetRewardId(poolDef);
        if (!string.IsNullOrWhiteSpace(poolId) &&
            _prototypes.TryIndex<NcSupplyRewardPoolPrototype>(poolId, out var supplyPoolProto) &&
            supplyPoolProto.Entries is { Count: > 0, } supplyOptions)
        {
            return TryValidateResolvedRewardPoolOptions(
                poolDef,
                ConvertSupplyRewardPoolEntries(supplyOptions),
                out options);
        }

        options = default!;
        return false;
    }

    private static List<ContractRewardDef> ConvertSupplyRewardPoolEntries(
        IReadOnlyList<NcSupplyRewardPoolEntry> entries
    )
    {
        var result = new List<ContractRewardDef>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            result.Add(
                new()
                {
                    Type = entry.Type,
                    RewardId = GetSupplyRewardEntryId(entry),
                    Count = entry.Count,
                    Weight = entry.Weight,
                    MaxRepeats = entry.MaxRepeats
                });
        }

        return result;
    }

    private static string GetSupplyRewardEntryId(NcSupplyRewardPoolEntry entry) =>
        entry.Type switch
        {
            StoreRewardType.Item => entry.Prototype,
            StoreRewardType.Currency => entry.Currency,
            StoreRewardType.Pool => entry.Pool,
            _ => string.Empty
        };

    private bool TryValidateResolvedRewardPoolOptions(
        ContractRewardDef poolDef,
        IReadOnlyList<ContractRewardDef> rawOptions,
        out List<ContractRewardDef> validOptions
    )
    {
        validOptions = new(rawOptions.Count);
        var poolId = GetRewardId(poolDef);
        var poolLabel = string.IsNullOrWhiteSpace(poolId) ? "<inline>" : poolId;

        for (var i = 0; i < rawOptions.Count; i++)
        {
            var def = rawOptions[i];
            var rewardId = GetRewardId(def);

            if (def.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Reward pool '{poolLabel}' entry #{i} has non-positive weight={def.Weight}.");
                continue;
            }

            if (def.Count.Min < 0 || def.Count.Max <= 0 || def.Count.Min > def.Count.Max)
            {
                Sawmill.Warning(
                    $"[Contracts] Reward pool '{poolLabel}' entry #{i} has invalid count range " +
                    $"{def.Count.Min}..{def.Count.Max}.");
                continue;
            }

            if (def.Type != StoreRewardType.Item &&
                def.Type != StoreRewardType.Currency &&
                def.Type != StoreRewardType.Pool)
            {
                Sawmill.Warning(
                    $"[Contracts] Reward pool '{poolLabel}' entry #{i} has unsupported reward type {def.Type}.");
                continue;
            }

            if (def.Type == StoreRewardType.Pool && string.IsNullOrWhiteSpace(rewardId))
            {
                Sawmill.Warning(
                    $"[Contracts] Reward pool '{poolLabel}' entry #{i} is Pool but has no pool id.");
                continue;
            }

            if ((def.Type == StoreRewardType.Item || def.Type == StoreRewardType.Currency) &&
                string.IsNullOrWhiteSpace(rewardId))
            {
                Sawmill.Warning(
                    $"[Contracts] Reward pool '{poolLabel}' entry #{i} has empty reward id.");
                continue;
            }

            validOptions.Add(def);
        }

        if (validOptions.Count > 0)
            return true;

        Sawmill.Warning($"[Contracts] Reward pool '{poolLabel}' has no valid entries after validation.");
        return false;
    }

    private static List<PoolEntry> CreateRewardPoolDeck(IReadOnlyList<ContractRewardDef> options)
    {
        var deck = new List<PoolEntry>(options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            var def = options[i];
            deck.Add(new(def, $"{i}:{def.Type}:{GetRewardId(def)}"));
        }

        return deck;
    }

    private bool TryRollRewardPoolEntry(
        EntityUid store,
        string contractProtoId,
        List<PoolEntry> deck,
        Dictionary<string, int> dropCounts,
        int depth,
        List<ContractRewardData> output
    )
    {
        if (deck.Count == 0)
            return false;

        var winner = PickWeighted(_random, deck, x => x.Def.Weight);
        var dropCount = IncrementRewardPoolDropCount(dropCounts, winner.Key);
        if (winner.Def.MaxRepeats > 0 && dropCount >= winner.Def.MaxRepeats)
            RemovePoolEntrySwap(deck, winner);

        output.AddRange(BakeRewardsRecursive(store, contractProtoId, new() { winner.Def, }, depth));
        return true;
    }

    private static void RemovePoolEntrySwap(List<PoolEntry> deck, PoolEntry entry)
    {
        for (var idx = 0; idx < deck.Count; idx++)
        {
            if (!deck[idx].Equals(entry))
                continue;

            var lastIndex = deck.Count - 1;
            if (idx != lastIndex)
                deck[idx] = deck[lastIndex];
            deck.RemoveAt(lastIndex);
            return;
        }
    }

    private static int IncrementRewardPoolDropCount(Dictionary<string, int> dropCounts, string key)
    {
        if (!dropCounts.TryAdd(key, 1))
            dropCounts[key] = dropCounts[key] + 1;

        return dropCounts[key];
    }

    private static string GetRewardId(ContractRewardDef reward) => reward.RewardId;

    private static List<ContractRewardData> AggregateRewards(List<ContractRewardData> rewards)
    {
        if (rewards.Count == 0)
            return rewards;

        var map = new Dictionary<(StoreRewardType Type, string Id), int>();

        foreach (var r in rewards)
        {
            if (r.Amount <= 0 || string.IsNullOrWhiteSpace(r.Id))
                continue;
            if (r.Type != StoreRewardType.Item && r.Type != StoreRewardType.Currency)
                continue;

            var k = (r.Type, r.Id);
            if (!map.TryAdd(k, r.Amount))
                map[k] = SaturatingAdd(map[k], r.Amount);
        }

        var outList = new List<ContractRewardData>(map.Count);
        foreach (var (k, amt) in map)
        {
            if (amt <= 0)
                continue;
            outList.Add(new(k.Type, k.Id, amt));
        }

        return outList;
    }
}
