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

        if (!TryBuildBarterCostPlan(user, listing.BarterCost, actual, out var costPlan))
            return false;

        if (!TryBuildBarterReceivePlan(listing, actual, out var receivePlan))
            return false;

        if (!TryExecuteBarterCostPlan(user, costPlan))
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
        return TryBuildBarterCostPlan(root, costs, times, out _);
    }

    private bool TryBuildBarterCostPlan(
        EntityUid root,
        List<NcBarterCostEntry> costs,
        int times,
        out BarterCostPlan plan)
    {
        plan = new();

        if (times <= 0 || costs.Count == 0)
            return false;

        if (!TryBuildBarterCostDemands(costs, times, out var demands))
            return false;

        var items = BuildBarterReservableItems(root);
        if (items.Count == 0)
            return false;

        demands.Sort((a, b) =>
        {
            var aUnits = CountAvailableUnitsForDemand(items, a);
            var bUnits = CountAvailableUnitsForDemand(items, b);
            var byUnits = aUnits.CompareTo(bUnits);
            if (byUnits != 0)
                return byUnits;

            return b.Required.CompareTo(a.Required);
        });

        for (var i = 0; i < demands.Count; i++)
        {
            if (!TryReserveBarterDemand(plan, items, demands[i]))
                return false;
        }

        return plan.Reservations.Count > 0;
    }

    private bool TryBuildBarterCostDemands(
        List<NcBarterCostEntry> costs,
        int times,
        out List<BarterCostDemand> demands)
    {
        demands = new(costs.Count);

        for (var i = 0; i < costs.Count; i++)
        {
            var cost = costs[i];
            if (!TryMultiplyPositive(cost.Count, times, out var required))
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
                if (!_protos.HasIndex<StackPrototype>(cost.Currency))
                    return false;

                demands.Add(new()
                {
                    Currency = cost.Currency,
                    Required = required
                });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Prototype))
            {
                if (!_protos.HasIndex<EntityPrototype>(cost.Prototype))
                    return false;

                demands.Add(new()
                {
                    Prototype = cost.Prototype,
                    PrototypeStackType = _inventory.GetProductStackType(cost.Prototype) ?? string.Empty,
                    Required = required
                });
                continue;
            }

            if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                return false;

            demands.Add(new()
            {
                Group = cost.Group,
                GroupPrototype = group,
                Required = required
            });
        }

        return demands.Count > 0;
    }

    private List<BarterReservableItem> BuildBarterReservableItems(EntityUid root)
    {
        var scanned = new List<EntityUid>();
        _inventory.ScanInventoryItems(root, scanned);

        var result = new List<BarterReservableItem>(scanned.Count);
        for (var i = 0; i < scanned.Count; i++)
        {
            var ent = scanned[i];
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;

            if (_inventory.IsProtectedFromDirectSale(root, ent))
                continue;

            if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                continue;

            if (_ents.TryGetComponent(ent, out StackComponent? stack))
            {
                var count = Math.Max(0, stack.Count);
                if (count <= 0)
                    continue;

                result.Add(new()
                {
                    Entity = ent,
                    Prototype = meta.EntityPrototype.ID,
                    StackType = stack.StackTypeId,
                    UnitsLeft = count,
                    IsStack = true
                });
                continue;
            }

            result.Add(new()
            {
                Entity = ent,
                Prototype = meta.EntityPrototype.ID,
                StackType = string.Empty,
                UnitsLeft = 1,
                IsStack = false
            });
        }

        return result;
    }

    private int CountAvailableUnitsForDemand(List<BarterReservableItem> items, BarterCostDemand demand)
    {
        var total = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.UnitsLeft <= 0)
                continue;

            if (BarterItemMatchesDemand(item, demand))
                total += item.UnitsLeft;
        }

        return total;
    }

    private bool TryReserveBarterDemand(
        BarterCostPlan plan,
        List<BarterReservableItem> items,
        BarterCostDemand demand)
    {
        var candidates = new List<BarterReservableItem>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.UnitsLeft <= 0)
                continue;

            if (BarterItemMatchesDemand(item, demand))
                candidates.Add(item);
        }

        candidates.Sort((a, b) => a.UnitsLeft.CompareTo(b.UnitsLeft));

        var left = demand.Required;
        for (var i = 0; i < candidates.Count && left > 0; i++)
        {
            var item = candidates[i];
            if (item.UnitsLeft <= 0)
                continue;

            var take = Math.Min(item.UnitsLeft, left);
            item.UnitsLeft -= take;
            left -= take;

            AddBarterCostReservation(plan, item.Entity, take, item.IsStack);
        }

        return left <= 0;
    }

    private bool BarterItemMatchesDemand(BarterReservableItem item, BarterCostDemand demand)
    {
        if (!string.IsNullOrWhiteSpace(demand.Currency))
            return item.StackType == demand.Currency;

        if (!string.IsNullOrWhiteSpace(demand.Prototype))
        {
            if (!string.IsNullOrWhiteSpace(demand.PrototypeStackType))
                return item.StackType == demand.PrototypeStackType;

            return item.Prototype == demand.Prototype;
        }

        if (!string.IsNullOrWhiteSpace(demand.Group) && demand.GroupPrototype != null)
            return _inventory.EntityMatchesItemGroup(item.Entity, demand.GroupPrototype);

        return false;
    }

    private static void AddBarterCostReservation(
        BarterCostPlan plan,
        EntityUid entity,
        int count,
        bool isStack)
    {
        if (entity == EntityUid.Invalid || count <= 0)
            return;

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            var existing = plan.Reservations[i];
            if (existing.Entity != entity)
                continue;

            existing.Count += count;
            existing.IsStack |= isStack;
            plan.Reservations[i] = existing;
            return;
        }

        plan.Reservations.Add(new(entity, count, isStack));
    }

    private bool TryExecuteBarterCostPlan(EntityUid root, BarterCostPlan plan)
    {
        if (plan.Reservations.Count == 0)
            return false;

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            if (!ValidateBarterCostReservation(root, plan.Reservations[i]))
                return false;
        }

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            var reservation = plan.Reservations[i];
            if (reservation.IsStack)
            {
                if (!_ents.TryGetComponent(reservation.Entity, out StackComponent? stack))
                    return false;

                var newCount = stack.Count - reservation.Count;
                _stacks.SetCount(reservation.Entity, Math.Max(0, newCount), stack);
                if (stack.Count <= 0)
                    _ents.DeleteEntity(reservation.Entity);

                continue;
            }

            _ents.DeleteEntity(reservation.Entity);
        }

        _inventory.InvalidateInventoryCache(root);
        return true;
    }

    private bool ValidateBarterCostReservation(EntityUid root, BarterCostReservation reservation)
    {
        if (reservation.Entity == EntityUid.Invalid || reservation.Count <= 0)
            return false;

        if (!_ents.EntityExists(reservation.Entity))
            return false;

        if (_inventory.IsProtectedFromDirectSale(root, reservation.Entity))
            return false;

        if (reservation.IsStack)
        {
            if (!_ents.TryGetComponent(reservation.Entity, out StackComponent? stack))
                return false;

            return stack.Count >= reservation.Count;
        }

        return reservation.Count == 1;
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

    private bool IsValidBarterRewardPoolEntry(ContractRewardDef reward)
    {
        if (reward.Type != StoreRewardType.Item && reward.Type != StoreRewardType.Currency)
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

    private sealed class BarterCostPlan
    {
        public readonly List<BarterCostReservation> Reservations = new();
    }

    private sealed class BarterReservableItem
    {
        public EntityUid Entity;
        public bool IsStack;
        public string Prototype = string.Empty;
        public string StackType = string.Empty;
        public int UnitsLeft;
    }

    private sealed class BarterCostDemand
    {
        public string Currency = string.Empty;
        public string Prototype = string.Empty;
        public string PrototypeStackType = string.Empty;
        public string Group = string.Empty;
        public NcItemGroupPrototype? GroupPrototype;
        public int Required;
    }

    private struct BarterCostReservation
    {
        public BarterCostReservation(EntityUid entity, int count, bool isStack)
        {
            Entity = entity;
            Count = count;
            IsStack = isStack;
        }

        public EntityUid Entity;
        public int Count;
        public bool IsStack;
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
