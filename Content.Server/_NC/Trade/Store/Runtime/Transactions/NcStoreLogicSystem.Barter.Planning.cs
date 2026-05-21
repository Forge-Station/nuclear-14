using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    public void PrepareBarterAvailabilityContext(EntityUid root, BarterAvailabilityContext context)
    {
        context.ScannedItems.Clear();
        _inventory.ScanInventoryItems(root, context.ScannedItems);
        PrepareBarterAvailabilityContext(root, context.ScannedItems, context);
    }

    public void PrepareBarterAvailabilityContext(
        EntityUid root,
        IReadOnlyList<EntityUid> scannedItems,
        BarterAvailabilityContext context
    )
    {
        context.Reset(root);
        FillBarterReservableItems(root, scannedItems, context.BaseItems);
    }

    private void EnsureBarterAvailabilityContext(EntityUid root, BarterAvailabilityContext context)
    {
        if (!context.Prepared || context.Root != root)
            PrepareBarterAvailabilityContext(root, context);
    }

    private int FindPlannedBarterCount(
        EntityUid user,
        NcStoreListingDef listing,
        int requested,
        BarterAvailabilityContext? context = null
    )
    {
        if (requested <= 0)
            return 0;

        if (CanTakeBarterCostFromRoot(user, listing.BarterCost, requested, context))
            return requested;

        if (requested == 1)
            return 0;

        var low = 0;
        var high = requested - 1;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (CanTakeBarterCostFromRoot(user, listing.BarterCost, mid, context))
                low = mid;
            else
                high = mid - 1;
        }

        return low;
    }

    private bool CanTakeBarterCostFromRoot(
        EntityUid root,
        List<NcBarterCostEntry> costs,
        int times,
        BarterAvailabilityContext? context = null
    ) =>
        TryBuildBarterCostPlan(root, costs, times, out _, context);

    private bool TryBuildBarterCostPlan(
        EntityUid root,
        List<NcBarterCostEntry> costs,
        int times,
        out BarterCostPlan plan,
        BarterAvailabilityContext? context = null
    )
    {
        plan = new();

        if (times <= 0 || costs.Count == 0)
            return false;

        if (!TryBuildBarterCostDemands(costs, times, out var demands))
            return false;

        var items = GetBarterReservableItemsForPlan(root, context);
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
            if (!TryReserveBarterDemand(plan, items, demands[i], context?.DemandCandidates))
                return false;

        return plan.Reservations.Count > 0;
    }

    public sealed class BarterAvailabilityContext
    {
        internal readonly List<BarterReservableItem> BaseItems = new();
        internal readonly List<BarterReservableItem> DemandCandidates = new();
        internal readonly List<EntityUid> ScannedItems = new();
        internal readonly List<BarterReservableItem> WorkItems = new();
        internal bool Prepared;
        internal EntityUid Root = EntityUid.Invalid;

        internal void Reset(EntityUid root)
        {
            Root = root;
            Prepared = true;
            BaseItems.Clear();
            WorkItems.Clear();
            DemandCandidates.Clear();
        }
    }
}
