using Content.Shared._NC.Trade;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private List<ContractRewardData> BakeRewardsForContract(
        EntityUid store,
        string contractProtoId,
        List<ContractRewardDef> rewards)
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
            var probability = GetRewardProbability(bp);

            if (probability < 1.0f && !_random.Prob(Math.Clamp(probability, 0f, 1f)))
                continue;

            var rewardId = GetRewardId(bp);
            var count = RollFair(
                new(QuasiKeyKind.RAmount, store, contractProtoId, $"{depth}:{i}:{bp.Type}:{rewardId}"),
                GetRewardAmountRange(bp),
                0);

            if (count <= 0)
                continue;

            var isPool = bp.Type == StoreRewardType.Pool || bp.Options is { Count: > 0 };

            if (isPool)
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
        {
            if (!TryRollRewardPoolEntry(store, contractProtoId, deck, dropCounts, depth, output))
                break;
        }

        return output;
    }

    private bool TryResolveRewardPoolOptions(ContractRewardDef poolDef, out List<ContractRewardDef> options)
    {
        if (poolDef.Options is { Count: > 0 } inlineOptions)
        {
            return TryValidateResolvedRewardPoolOptions(poolDef, inlineOptions, out options);
        }

        var poolId = GetRewardId(poolDef);
        if (!string.IsNullOrWhiteSpace(poolId) &&
            _prototypes.TryIndex<NcSupplyRewardPoolPrototype>(poolId, out var supplyPoolProto) &&
            supplyPoolProto.Entries is { Count: > 0 } supplyOptions)
        {
            return TryValidateResolvedRewardPoolOptions(
                poolDef,
                ConvertSupplyRewardPoolEntries(supplyOptions),
                out options);
        }

        options = default!;
        return false;
    }

    private static List<ContractRewardDef> ConvertSupplyRewardPoolEntries(IReadOnlyList<NcSupplyRewardPoolEntry> entries)
    {
        var result = new List<ContractRewardDef>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            result.Add(new ContractRewardDef
            {
                Type = entry.Type,
                Prototype = entry.Prototype,
                Currency = entry.Currency,
                Pool = entry.Pool,
                Count = entry.Count,
                Weight = entry.Weight,
                MaxRepeats = entry.MaxRepeats
            });
        }

        return result;
    }

    private bool TryValidateResolvedRewardPoolOptions(
        ContractRewardDef poolDef,
        IReadOnlyList<ContractRewardDef> rawOptions,
        out List<ContractRewardDef> validOptions)
    {
        validOptions = new List<ContractRewardDef>(rawOptions.Count);
        var poolId = GetRewardId(poolDef);
        var poolLabel = string.IsNullOrWhiteSpace(poolId) ? "<inline>" : poolId;

        for (var i = 0; i < rawOptions.Count; i++)
        {
            var def = rawOptions[i];
            var rewardId = GetRewardId(def);

            if (def.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolLabel}' entry #{i} has non-positive weight={def.Weight}.");
                continue;
            }

            var amountRange = GetRewardAmountRange(def);
            if (amountRange.Min < 0 || amountRange.Max <= 0 || amountRange.Min > amountRange.Max)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolLabel}' entry #{i} has invalid count/amount range " +
                    $"{amountRange.Min}..{amountRange.Max}.");
                continue;
            }

            var probability = GetRewardProbability(def);
            if (probability < 0f || probability > 1f)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolLabel}' entry #{i} has invalid chance={probability}. Expected 0..1.");
                continue;
            }

            if (def.Type != StoreRewardType.Item &&
                def.Type != StoreRewardType.Currency &&
                def.Type != StoreRewardType.Pool)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolLabel}' entry #{i} has unsupported reward type {def.Type}.");
                continue;
            }

            if (def.Type == StoreRewardType.Pool && string.IsNullOrWhiteSpace(rewardId))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolLabel}' entry #{i} is Pool but has no pool id.");
                continue;
            }

            if ((def.Type == StoreRewardType.Item || def.Type == StoreRewardType.Currency) &&
                string.IsNullOrWhiteSpace(rewardId))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolLabel}' entry #{i} has empty reward id.");
                continue;
            }

            validOptions.Add(def);
        }

        if (validOptions.Count > 0)
            return true;

        Sawmill.Warning($"[ContractsV2] Reward pool '{poolLabel}' has no valid entries after validation.");
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
        List<ContractRewardData> output)
    {
        if (deck.Count == 0)
            return false;

        var winner = PickWeighted(_random, deck, x => x.Def.Weight);
        var dropCount = IncrementRewardPoolDropCount(dropCounts, winner.Key);
        if (winner.Def.MaxRepeats > 0 && dropCount >= winner.Def.MaxRepeats)
            RemovePoolEntrySwap(deck, winner);

        output.AddRange(BakeRewardsRecursive(store, contractProtoId, new() { winner.Def }, depth));
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

    private static string GetRewardId(ContractRewardDef reward)
    {
        if (!string.IsNullOrWhiteSpace(reward.Id))
            return reward.Id;

        return reward.Type switch
        {
            StoreRewardType.Item => reward.Prototype,
            StoreRewardType.Currency => reward.Currency,
            StoreRewardType.Pool => reward.Pool,
            _ => string.Empty
        };
    }

    private static float GetRewardProbability(ContractRewardDef reward)
    {
        return reward.Chance >= 0f ? reward.Chance : reward.Probability;
    }

    private static IntRange GetRewardAmountRange(ContractRewardDef reward)
    {
        return reward.Count.Min > 0 || reward.Count.Max > 0
            ? reward.Count
            : reward.Amount;
    }

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
