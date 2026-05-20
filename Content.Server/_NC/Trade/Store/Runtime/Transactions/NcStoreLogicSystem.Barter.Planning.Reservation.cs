using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
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
            if (!string.IsNullOrWhiteSpace(cost.TagTarget))
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

            if (!string.IsNullOrWhiteSpace(cost.Group))
            {
                if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                    return false;

                demands.Add(new()
                {
                    Group = cost.Group,
                    GroupPrototype = group,
                    Required = required
                });
                continue;
            }

            if (!_protos.TryIndex<NcTradeTagPrototype>(cost.TagTarget, out var tagTarget) ||
                string.IsNullOrWhiteSpace(tagTarget.Tag) ||
                !_protos.HasIndex<TagPrototype>(tagTarget.Tag))
                return false;

            demands.Add(new()
            {
                Tag = tagTarget.Tag,
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
        FillBarterReservableItems(root, scanned, result);
        return result;
    }

    private void FillBarterReservableItems(
        EntityUid root,
        IReadOnlyList<EntityUid> scanned,
        List<BarterReservableItem> result)
    {
        var write = 0;
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

                GetOrAddBarterReservableItem(result, write++).Set(
                    ent,
                    isStack: true,
                    meta.EntityPrototype.ID,
                    stack.StackTypeId,
                    count);
                continue;
            }

            GetOrAddBarterReservableItem(result, write++).Set(
                ent,
                isStack: false,
                meta.EntityPrototype.ID,
                string.Empty,
                1);
        }

        if (write < result.Count)
            result.RemoveRange(write, result.Count - write);
    }

    private List<BarterReservableItem> GetBarterReservableItemsForPlan(
        EntityUid root,
        BarterAvailabilityContext? context)
    {
        if (context == null)
            return BuildBarterReservableItems(root);

        EnsureBarterAvailabilityContext(root, context);
        CopyBarterReservableItems(context.BaseItems, context.WorkItems);
        return context.WorkItems;
    }

    private static void CopyBarterReservableItems(
        List<BarterReservableItem> source,
        List<BarterReservableItem> target)
    {
        for (var i = 0; i < source.Count; i++)
            GetOrAddBarterReservableItem(target, i).CopyFrom(source[i]);

        if (source.Count < target.Count)
            target.RemoveRange(source.Count, target.Count - source.Count);
    }

    private static BarterReservableItem GetOrAddBarterReservableItem(
        List<BarterReservableItem> items,
        int index)
    {
        if (index < items.Count)
            return items[index];

        var item = new BarterReservableItem();
        items.Add(item);
        return item;
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
        BarterCostDemand demand,
        List<BarterReservableItem>? candidatesScratch = null)
    {
        var candidates = candidatesScratch ?? new List<BarterReservableItem>();
        candidates.Clear();

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

        if (!string.IsNullOrWhiteSpace(demand.Tag))
            return _inventory.PrototypeHasTag(item.Prototype, demand.Tag);

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
}
