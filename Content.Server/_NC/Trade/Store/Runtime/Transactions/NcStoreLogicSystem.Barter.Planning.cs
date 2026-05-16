using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
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

            AddBarterCostReservation(plan, item, take);
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
        BarterReservableItem item,
        int count)
    {
        if (item.Entity == EntityUid.Invalid || count <= 0)
            return;

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            var existing = plan.Reservations[i];
            if (existing.Entity != item.Entity)
                continue;

            existing.Count += count;
            existing.IsStack |= item.IsStack;
            plan.Reservations[i] = existing;
            return;
        }

        plan.Reservations.Add(
            new(
                item.Entity,
                count,
                item.IsStack,
                item.Prototype,
                item.StackType));
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
        public BarterCostReservation(
            EntityUid entity,
            int count,
            bool isStack,
            string prototype,
            string stackType)
        {
            Entity = entity;
            Count = count;
            IsStack = isStack;
            Prototype = prototype;
            StackType = stackType;
        }

        public EntityUid Entity;
        public int Count;
        public bool IsStack;
        public string Prototype;
        public string StackType;
    }

}
