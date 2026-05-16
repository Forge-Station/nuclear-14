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
            var refunded = TryRefundBarterCostPlan(user, costPlan);
            Sawmill.Warning(
                $"[NcStore] Barter '{listing.Id}' consumed cost but failed to execute the prebuilt receive plan. " +
                $"Cost refund {(refunded ? "completed" : "was incomplete")}. " +
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
}
