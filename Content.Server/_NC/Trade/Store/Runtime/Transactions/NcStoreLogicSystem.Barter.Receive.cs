using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private const int MaxBarterReceivePoolDepth = 6;

    private bool TryBuildBarterReceivePlan(NcStoreListingDef listing, int times, out BarterReceivePlan plan)
    {
        plan = new();

        if (times <= 0)
            return false;

        for (var i = 0; i < listing.BarterReceive.Count; i++)
        {
            var receive = listing.BarterReceive[i];
            if (!TryMultiplyPositive(receive.Count, times, out var amount))
                return false;

            var sources = 0;
            if (!string.IsNullOrWhiteSpace(receive.Currency))
                sources++;
            if (!string.IsNullOrWhiteSpace(receive.Prototype))
                sources++;

            if (sources != 1)
                return false;

            if (!string.IsNullOrWhiteSpace(receive.Currency))
            {
                if (!_protos.HasIndex<StackPrototype>(receive.Currency))
                    return false;

                AddReceivePlanEntry(plan, string.Empty, receive.Currency, amount);
                continue;
            }

            if (string.IsNullOrWhiteSpace(receive.Prototype) ||
                !_protos.HasIndex<EntityPrototype>(receive.Prototype))
                return false;

            AddReceivePlanEntry(plan, receive.Prototype, string.Empty, amount);
        }

        for (var i = 0; i < listing.BarterReceivePools.Count; i++)
            if (!TryAddBarterReceivePoolToPlan(plan, listing.BarterReceivePools[i], times))
                return false;

        // If a barter has only random receive pools and every chance roll misses, the transaction is
        // treated as not available for this click. This avoids charging the player for an empty result.
        return plan.Entries.Count > 0;
    }

    private bool TryAddBarterReceivePoolToPlan(
        BarterReceivePlan plan,
        NcBarterReceivePoolEntry entry,
        int times
    )
    {
        if (times <= 0)
            return false;

        if (entry.Chance < 0f || entry.Chance > 1f)
            return false;

        if (entry.Rolls.Min <= 0 || entry.Rolls.Max <= 0 || entry.Rolls.Min > entry.Rolls.Max)
            return false;

        if (!TryMultiplyPositive(entry.Rolls.Max, times, out _))
            return false;

        if (!TryCreateValidBarterRewardDeck(entry.Pool, out var deck) || deck.Count == 0)
            return false;

        var dropCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var trade = 0; trade < times; trade++)
        {
            if (entry.Chance < 1f && !_random.Prob(entry.Chance))
                continue;

            var rolls = RollRange(entry.Rolls);
            for (var roll = 0; roll < rolls; roll++)
                if (!TryRollBarterRewardToPlan(plan, deck, dropCounts, 0))
                    break;
        }

        return true;
    }

    private bool TryCreateValidBarterRewardDeck(string poolId, out List<ContractRewardDef> deck)
    {
        return TryCreateValidBarterRewardDeck(
            poolId,
            out deck,
            new HashSet<string>(StringComparer.Ordinal),
            0);
    }

    private bool TryCreateValidBarterRewardDeck(
        string poolId,
        out List<ContractRewardDef> deck,
        HashSet<string> visited,
        int depth)
    {
        deck = new();
        if (string.IsNullOrWhiteSpace(poolId) || depth > MaxBarterReceivePoolDepth)
            return false;

        if (!_protos.TryIndex<NcSupplyRewardPoolPrototype>(poolId, out var supplyPool))
            return false;

        if (!visited.Add(poolId))
            return false;

        deck = CreateValidBarterRewardDeck(supplyPool, visited, depth);
        visited.Remove(poolId);
        return deck.Count > 0;
    }

    private List<ContractRewardDef> CreateValidBarterRewardDeck(
        NcSupplyRewardPoolPrototype pool,
        HashSet<string> visited,
        int depth)
    {
        var result = new List<ContractRewardDef>(pool.Entries.Count);
        for (var i = 0; i < pool.Entries.Count; i++)
        {
            var reward = ToContractRewardDef(pool.Entries[i]);
            if (IsValidBarterRewardPoolEntry(reward, visited, depth))
                result.Add(reward);
        }

        return result;
    }

    private static ContractRewardDef ToContractRewardDef(NcSupplyRewardPoolEntry entry)
    {
        return new ContractRewardDef
        {
            Type = entry.Type,
            Prototype = entry.Prototype,
            Currency = entry.Currency,
            Pool = entry.Pool,
            Count = entry.Count,
            Weight = entry.Weight,
            MaxRepeats = entry.MaxRepeats,
            Chance = 1f,
            Probability = 1f
        };
    }

    private bool TryRollBarterRewardToPlan(
        BarterReceivePlan plan,
        List<ContractRewardDef> deck,
        Dictionary<string, int> dropCounts,
        int depth
    )
    {
        if (deck.Count == 0 || depth > MaxBarterReceivePoolDepth)
            return false;

        if (!TryPickWeightedReward(deck, out var reward))
            return false;

        var key = $"{reward.Type}:{GetRewardId(reward)}";
        dropCounts.TryGetValue(key, out var previousDrops);
        var nextDrop = previousDrops + 1;
        dropCounts[key] = nextDrop;

        if (reward.MaxRepeats > 0 && nextDrop >= reward.MaxRepeats)
            deck.Remove(reward);

        var chance = GetRewardChance(reward);
        if (chance < 1f && !_random.Prob(chance))
            return true;

        var rewardId = GetRewardId(reward);
        var amount = RollRange(GetRewardAmountRange(reward));
        if (amount <= 0 || string.IsNullOrWhiteSpace(rewardId))
            return true;

        if (reward.Type == StoreRewardType.Currency)
        {
            if (!_protos.HasIndex<StackPrototype>(rewardId))
                return false;

            AddReceivePlanEntry(plan, string.Empty, rewardId, amount);
            return true;
        }

        if (reward.Type == StoreRewardType.Item)
        {
            if (!_protos.HasIndex<EntityPrototype>(rewardId))
                return false;

            AddReceivePlanEntry(plan, rewardId, string.Empty, amount);
            return true;
        }

        if (reward.Type == StoreRewardType.Pool)
        {
            if (!TryCreateValidBarterRewardDeck(rewardId, out var nestedDeck))
                return false;

            for (var i = 0; i < amount; i++)
                if (!TryRollBarterRewardToPlan(plan, nestedDeck, dropCounts, depth + 1))
                    break;

            return true;
        }

        return false;
    }

    private static void AddReceivePlanEntry(
        BarterReceivePlan plan,
        string prototype,
        string currency,
        int amount
    )
    {
        if (amount <= 0)
            return;

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            var existing = plan.Entries[i];
            if (existing.Prototype != prototype || existing.Currency != currency)
                continue;

            var total = (long) existing.Count + amount;
            existing.Count = total > int.MaxValue ? int.MaxValue : (int) total;
            return;
        }

        plan.Entries.Add(
            new()
            {
                Prototype = prototype,
                Currency = currency,
                Count = amount
            });
    }

    private bool TryExecuteBarterReceivePlan(EntityUid user, BarterReceivePlan plan)
    {
        if (!TryBuildRewardExecutionPlan(plan, out var rewardPlan, out var reason))
        {
            Sawmill.Warning($"[NcStore] Failed to build barter receive reward plan: {reason}");
            return false;
        }

        if (!TryExecuteRewardExecutionPlan(user, rewardPlan, "BarterReceive", out reason))
        {
            Sawmill.Warning($"[NcStore] Failed to execute barter receive reward plan: {reason}");
            return false;
        }

        return true;
    }

    private bool IsValidBarterRewardPoolEntry(
        ContractRewardDef reward,
        HashSet<string> visited,
        int depth)
    {
        if (reward.Type != StoreRewardType.Item &&
            reward.Type != StoreRewardType.Currency &&
            reward.Type != StoreRewardType.Pool)
            return false;

        if (reward.Weight <= 0)
            return false;

        var amountRange = GetRewardAmountRange(reward);
        if (amountRange.Min < 0 || amountRange.Max <= 0 || amountRange.Min > amountRange.Max)
            return false;

        var chance = GetRewardChance(reward);
        if (chance < 0f || chance > 1f)
            return false;

        var rewardId = GetRewardId(reward);
        if (string.IsNullOrWhiteSpace(rewardId))
            return false;

        return reward.Type switch
        {
            StoreRewardType.Item => _protos.HasIndex<EntityPrototype>(rewardId),
            StoreRewardType.Currency => _protos.HasIndex<StackPrototype>(rewardId),
            StoreRewardType.Pool => TryCreateValidBarterRewardDeck(rewardId, out _, visited, depth + 1),
            _ => false
        };
    }

    private bool TryPickWeightedReward(List<ContractRewardDef> deck, out ContractRewardDef reward)
    {
        reward = default!;
        var total = 0;
        for (var i = 0; i < deck.Count; i++)
        {
            var weight = Math.Max(0, deck[i].Weight);
            total += weight;
        }

        if (total <= 0)
            return false;

        var roll = _random.Next(total);
        for (var i = 0; i < deck.Count; i++)
        {
            var weight = Math.Max(0, deck[i].Weight);
            if (roll < weight)
            {
                reward = deck[i];
                return true;
            }

            roll -= weight;
        }

        reward = deck[^1];
        return true;
    }

    private int RollRange(IntRange range)
    {
        if (range.Min <= 0 || range.Max <= 0)
            return 0;

        var min = Math.Min(range.Min, range.Max);
        var max = Math.Max(range.Min, range.Max);
        if (min == max)
            return min;

        return min + _random.Next(max - min + 1);
    }


    private static IntRange GetRewardAmountRange(ContractRewardDef reward)
    {
        return reward.Count.Min > 0 || reward.Count.Max > 0
            ? reward.Count
            : reward.Amount;
    }
    private static float GetRewardChance(ContractRewardDef reward) =>
        reward.Chance >= 0f ? reward.Chance : reward.Probability;

    private static string GetRewardId(ContractRewardDef reward)
    {
        if (!string.IsNullOrWhiteSpace(reward.Prototype))
            return reward.Prototype;

        if (!string.IsNullOrWhiteSpace(reward.Currency))
            return reward.Currency;

        if (!string.IsNullOrWhiteSpace(reward.Pool))
            return reward.Pool;

        return reward.Id;
    }

    private sealed class BarterReceivePlan
    {
        public readonly List<BarterReceivePlanEntry> Entries = new();
    }

    private sealed class BarterReceivePlanEntry
    {
        public int Count;
        public string Currency = string.Empty;
        public string Prototype = string.Empty;
    }
}
