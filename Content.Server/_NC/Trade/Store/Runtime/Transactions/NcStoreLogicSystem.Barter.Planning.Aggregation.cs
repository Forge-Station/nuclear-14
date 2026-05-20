using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private bool TryBuildAggregatedBarterCost(
        NcStoreListingDef listing,
        out List<NcBarterCostEntry> aggregated
    )
    {
        aggregated = new();

        var currencies = new Dictionary<string, int>(StringComparer.Ordinal);
        var prototypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        var tags = new Dictionary<string, int>(StringComparer.Ordinal);

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
            if (!string.IsNullOrWhiteSpace(cost.TagTarget))
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
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.TagTarget))
            {
                if (!TryAddAggregatedCost(tags, cost.TagTarget, cost.Count))
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

        foreach (var (tagTarget, count) in tags)
            aggregated.Add(
                new()
                {
                    TagTarget = tagTarget,
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

        if (!string.IsNullOrWhiteSpace(cost.TagTarget))
        {
            var owned = _inventory.GetOwnedFromSnapshot(snapshot, cost.TagTarget, PrototypeMatchMode.Tag);
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

    internal sealed class BarterReservableItem
    {
        public EntityUid Entity;
        public bool IsStack;
        public string Prototype = string.Empty;
        public string StackType = string.Empty;
        public int UnitsLeft;

        public void Set(EntityUid entity, bool isStack, string prototype, string stackType, int unitsLeft)
        {
            Entity = entity;
            IsStack = isStack;
            Prototype = prototype;
            StackType = stackType;
            UnitsLeft = unitsLeft;
        }

        public void CopyFrom(BarterReservableItem other)
        {
            Entity = other.Entity;
            IsStack = other.IsStack;
            Prototype = other.Prototype;
            StackType = other.StackType;
            UnitsLeft = other.UnitsLeft;
        }
    }

    private sealed class BarterCostDemand
    {
        public string Currency = string.Empty;
        public string Prototype = string.Empty;
        public string PrototypeStackType = string.Empty;
        public string Group = string.Empty;
        public string Tag = string.Empty;
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
