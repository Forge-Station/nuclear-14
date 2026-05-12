using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    public int GetMaxBarterCountFromSnapshot(NcStoreListingDef listing, in NcInventorySnapshot snapshot)
    {
        if (listing.Mode != StoreMode.Barter ||
            listing.BarterCost.Count == 0 ||
            listing.BarterReceive.Count == 0 && listing.BarterReceivePools.Count == 0)
            return 0;

        if (!TryBuildAggregatedBarterCost(listing, out var aggregatedCosts))
            return 0;

        var max = int.MaxValue;

        for (var i = 0; i < aggregatedCosts.Count; i++)
        {
            var cost = aggregatedCosts[i];
            if (!TryGetAffordableBarterUnitsFromSnapshot(cost, snapshot, out var possible))
                return 0;

            max = Math.Min(max, possible);
            if (max <= 0)
                return 0;
        }

        if (listing.RemainingCount >= 0)
            max = Math.Min(max, listing.RemainingCount);

        return Math.Max(0, max);
    }

    public int GetMaxBarterCount(EntityUid user, NcStoreListingDef listing, in NcInventorySnapshot snapshot)
    {
        var upper = GetMaxBarterCountFromSnapshot(listing, snapshot);
        if (upper <= 0)
            return 0;

        return FindPlannedBarterCount(user, listing, upper);
    }

    public bool TryBarter(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Barter, listingId),
            out var listing))
            return false;

        if (listing.BarterCost.Count == 0 ||
            listing.BarterReceive.Count == 0 && listing.BarterReceivePools.Count == 0)
            return false;

        _inventory.InvalidateInventoryCache(user);
        var snapshot = _inventory.BuildInventorySnapshot(user);
        var maxPossible = GetMaxBarterCount(user, listing, snapshot);
        if (maxPossible <= 0)
            return false;

        var requested = Math.Min(count, maxPossible);
        var actual = FindPlannedBarterCount(user, listing, requested);
        if (actual <= 0)
            return false;

        if (!TryBuildBarterReceivePlan(listing, actual, out var receivePlan))
            return false;

        if (!TryTakeBarterCostFromRoot(user, listing.BarterCost, actual))
            return false;

        if (!TryExecuteBarterReceivePlan(user, receivePlan))
        {
            Sawmill.Warning(
                $"[NcStore] Barter '{listing.Id}' consumed cost but failed to execute the prebuilt receive plan. " +
                "Check receive prototypes/currencies and spawn coordinates.");
            _inventory.InvalidateInventoryCache(user);
            return false;
        }

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - actual);

        _inventory.InvalidateInventoryCache(user);
        Sawmill.Info($"TryBarter: OK listing='{listing.Id}' x{actual}");
        return true;
    }

    private int FindPlannedBarterCount(EntityUid user, NcStoreListingDef listing, int requested)
    {
        if (requested <= 0)
            return 0;

        var low = 0;
        var high = requested;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (CanTakeBarterCostFromRoot(user, listing.BarterCost, mid))
                low = mid;
            else
                high = mid - 1;
        }

        return low;
    }

    private bool CanTakeBarterCostFromRoot(EntityUid root, List<NcBarterCostEntry> costs, int times)
    {
        if (times <= 0)
            return true;

        _inventory.InvalidateInventoryCache(root);
        var snapshot = _inventory.BuildInventorySnapshot(root);

        foreach (var cost in costs)
        {
            if (!TryMultiplyPositive(cost.Count, times, out var required))
                return false;

            if (!string.IsNullOrWhiteSpace(cost.Currency))
            {
                var have = snapshot.StackTypeCounts.TryGetValue(cost.Currency, out var balance) ? balance : 0;
                if (have < required)
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Prototype))
            {
                var have = _inventory.GetOwnedFromSnapshot(snapshot, cost.Prototype, PrototypeMatchMode.Exact);
                if (have < required)
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Group))
            {
                if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                    return false;

                var have = _inventory.GetOwnedFromSnapshotForItemGroup(snapshot, group);
                if (have < required)
                    return false;
                continue;
            }

            return false;
        }

        return true;
    }

    private bool TryTakeBarterCostFromRoot(EntityUid root, List<NcBarterCostEntry> costs, int times)
    {
        if (times <= 0)
            return false;

        if (!CanTakeBarterCostFromRoot(root, costs, times))
            return false;

        foreach (var cost in costs)
        {
            if (!TryMultiplyPositive(cost.Count, times, out var required))
                return false;

            if (!string.IsNullOrWhiteSpace(cost.Currency))
            {
                if (!TryTakeCurrency(root, cost.Currency, required))
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Prototype))
            {
                if (!_inventory.TryTakeProductUnitsFromRootCached(
                    root,
                    cost.Prototype,
                    required,
                    PrototypeMatchMode.Exact))
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Group))
            {
                if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                    return false;

                if (!_inventory.TryTakeItemGroupUnitsFromRootCached(root, group, required))
                    return false;
                continue;
            }

            return false;
        }

        return true;
    }

    private bool TryBuildAggregatedBarterCost(
        NcStoreListingDef listing,
        out List<NcBarterCostEntry> aggregated
    )
    {
        aggregated = new();

        var currencies = new Dictionary<string, int>(StringComparer.Ordinal);
        var prototypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < listing.BarterCost.Count; i++)
        {
            var cost = listing.BarterCost[i];
            if (cost.Count <= 0)
                return false;

            var sources = 0;
            if (!string.IsNullOrWhiteSpace(cost.Currency))
                sources++;
            if (!string.IsNullOrWhiteSpace(cost.Prototype))
                sources++;
            if (!string.IsNullOrWhiteSpace(cost.Group))
                sources++;

            if (sources != 1)
                return false;

            if (!string.IsNullOrWhiteSpace(cost.Currency))
            {
                if (!TryAddAggregatedCost(currencies, cost.Currency, cost.Count))
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Prototype))
            {
                if (!TryAddAggregatedCost(prototypes, cost.Prototype, cost.Count))
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Group))
            {
                if (!TryAddAggregatedCost(groups, cost.Group, cost.Count))
                    return false;
            }
        }

        foreach (var (currency, count) in currencies)
            aggregated.Add(
                new()
                {
                    Currency = currency,
                    Count = count
                });

        foreach (var (prototype, count) in prototypes)
            aggregated.Add(
                new()
                {
                    Prototype = prototype,
                    Count = count
                });

        foreach (var (group, count) in groups)
            aggregated.Add(
                new()
                {
                    Group = group,
                    Count = count
                });

        return aggregated.Count > 0;
    }

    private static bool TryAddAggregatedCost(Dictionary<string, int> target, string id, int count)
    {
        if (string.IsNullOrWhiteSpace(id) || count <= 0)
            return false;

        target.TryGetValue(id, out var previous);
        var total = (long) previous + count;
        if (total <= 0 || total > int.MaxValue)
            return false;

        target[id] = (int) total;
        return true;
    }

    private bool TryGetAffordableBarterUnitsFromSnapshot(
        NcBarterCostEntry cost,
        in NcInventorySnapshot snapshot,
        out int possible
    )
    {
        possible = 0;

        if (cost.Count <= 0)
            return false;

        if (!string.IsNullOrWhiteSpace(cost.Currency))
        {
            var balance = snapshot.StackTypeCounts.TryGetValue(cost.Currency, out var cur) ? cur : 0;
            possible = balance / cost.Count;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(cost.Prototype))
        {
            var owned = _inventory.GetOwnedFromSnapshot(snapshot, cost.Prototype, PrototypeMatchMode.Exact);
            possible = owned / cost.Count;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(cost.Group))
        {
            if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                return false;

            var owned = _inventory.GetOwnedFromSnapshotForItemGroup(snapshot, group);
            possible = owned / cost.Count;
            return true;
        }

        return false;
    }

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

        if (!_protos.TryIndex<NcContractRewardPoolPrototype>(entry.Pool, out var pool) || pool.Entries.Count == 0)
            return false;

        var deck = CreateValidBarterRewardDeck(pool);
        if (deck.Count == 0)
            return false;

        var dropCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var trade = 0; trade < times; trade++)
        {
            if (entry.Chance < 1f && !_random.Prob(entry.Chance))
                continue;

            var rolls = RollRange(entry.Rolls);
            for (var roll = 0; roll < rolls; roll++)
                if (!TryRollBarterRewardToPlan(plan, deck, dropCounts))
                    break;
        }

        return true;
    }

    private List<ContractRewardDef> CreateValidBarterRewardDeck(NcContractRewardPoolPrototype pool)
    {
        var result = new List<ContractRewardDef>(pool.Entries.Count);
        for (var i = 0; i < pool.Entries.Count; i++)
        {
            var reward = pool.Entries[i];
            if (IsValidBarterRewardPoolEntry(reward))
                result.Add(reward);
        }

        return result;
    }

    private bool TryRollBarterRewardToPlan(
        BarterReceivePlan plan,
        List<ContractRewardDef> deck,
        Dictionary<string, int> dropCounts
    )
    {
        if (deck.Count == 0)
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
        var amount = RollRange(reward.Amount);
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
        if (plan.Entries.Count == 0)
            return false;

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            var entry = plan.Entries[i];
            if (entry.Count <= 0)
                return false;

            if (!string.IsNullOrWhiteSpace(entry.Currency))
            {
                if (!_protos.HasIndex<StackPrototype>(entry.Currency))
                    return false;

                GiveCurrency(user, entry.Currency, entry.Count);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.Prototype))
            {
                if (!_protos.HasIndex<EntityPrototype>(entry.Prototype))
                    return false;

                var spawned = TrySpawnProductUnits(entry.Prototype, user, entry.Count);
                if (spawned < entry.Count)
                    return false;

                continue;
            }

            return false;
        }

        return true;
    }

    private bool ValidateBarterReceiveEntries(NcStoreListingDef listing, int times)
    {
        if (times <= 0)
            return false;

        for (var i = 0; i < listing.BarterReceive.Count; i++)
        {
            var receive = listing.BarterReceive[i];
            if (!TryMultiplyPositive(receive.Count, times, out _))
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

                continue;
            }

            if (string.IsNullOrWhiteSpace(receive.Prototype) || !_protos.HasIndex<EntityPrototype>(receive.Prototype))
                return false;
        }

        for (var i = 0; i < listing.BarterReceivePools.Count; i++)
            if (!ValidateBarterReceivePoolEntry(listing.BarterReceivePools[i], times))
                return false;

        return true;
    }

    private bool ValidateBarterReceivePoolEntry(NcBarterReceivePoolEntry entry, int times)
    {
        if (times <= 0)
            return false;

        if (entry.Chance < 0f || entry.Chance > 1f)
            return false;

        if (entry.Rolls.Min <= 0 || entry.Rolls.Max <= 0 || entry.Rolls.Min > entry.Rolls.Max)
            return false;

        if (!TryMultiplyPositive(entry.Rolls.Max, times, out _))
            return false;

        if (!_protos.TryIndex<NcContractRewardPoolPrototype>(entry.Pool, out var pool) || pool.Entries.Count == 0)
            return false;

        for (var i = 0; i < pool.Entries.Count; i++)
            if (IsValidBarterRewardPoolEntry(pool.Entries[i]))
                return true;

        return false;
    }

    private bool IsValidBarterRewardPoolEntry(ContractRewardDef reward)
    {
        if (reward.Type != StoreRewardType.Item && reward.Type != StoreRewardType.Currency)
            return false;

        if (reward.Weight <= 0)
            return false;

        if (reward.Amount.Min <= 0 || reward.Amount.Max <= 0 || reward.Amount.Min > reward.Amount.Max)
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

    private static bool TryMultiplyPositive(int left, int right, out int result)
    {
        result = 0;
        if (left <= 0 || right <= 0)
            return false;

        var value = (long) left * right;
        if (value <= 0 || value > int.MaxValue)
            return false;

        result = (int) value;
        return true;
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
