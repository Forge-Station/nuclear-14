using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public bool TryMassSellFromContainer(EntityUid machine, NcStoreComponent store, EntityUid user, EntityUid container)
    {
        if (store.Listings.Count == 0)
            return false;

        _inventory.InvalidateInventoryCache(container);
        var items = new List<EntityUid>();
        _inventory.ScanInventoryItems(container, items);

        var plan = ComputeMassSellPlanFromCachedItems(machine, store, container, items);
        if (plan.Steps.Count == 0 || plan.IncomeByCurrency.Count == 0)
            return false;

        var rewards = _transactionCoordinator.BuildCurrencyRewards(plan.IncomeByCurrency);
        if (rewards.Count == 0)
            return false;

        if (!TryExecuteRewardListWithPreCommit(
                user,
                rewards,
                "MassSell",
                () => TryCommitMassSellTake(container, items, plan),
                out var reason))
        {
            Sawmill.Warning($"[NcStore] TryMassSellFromContainer failed: {reason}");
            return false;
        }

        ApplyMassSellListingRemaining(plan);
        _inventory.InvalidateInventoryCache(container);
        _inventory.InvalidateInventoryCache(user);
        return true;
    }

    private string? TryCommitMassSellTake(
        EntityUid container,
        List<EntityUid> items,
        MassSellPlan plan)
    {
        return _transactionCoordinator.TryCommitInventoryTake(
            "MassSell",
            () =>
            {
                for (var i = 0; i < plan.Steps.Count; i++)
                {
                    var step = plan.Steps[i];
                    if (step.Count <= 0 ||
                        step.UnitPrice <= 0 ||
                        string.IsNullOrWhiteSpace(step.CurrencyId) ||
                        string.IsNullOrWhiteSpace(step.Listing.ProductEntity))
                    {
                        return $"invalid mass-sell step #{i}";
                    }

                    var listing = step.Listing;
                    if (listing.RemainingCount >= 0 && listing.RemainingCount < step.Count)
                        return $"listing '{listing.Id}' has only {listing.RemainingCount} units remaining for planned sell x{step.Count}";

                    if (!_inventory.TryTakeProductUnitsFromCachedList(
                            container,
                            items,
                            listing.ProductEntity,
                            step.Count,
                            listing.MatchMode))
                    {
                        return $"failed to consume mass-sell product '{listing.ProductEntity}' x{step.Count}";
                    }
                }

                return null;
            });
    }

    private static void ApplyMassSellListingRemaining(MassSellPlan plan)
    {
        foreach (var step in plan.Steps)
        {
            var listing = step.Listing;
            if (listing.RemainingCount > 0)
                listing.RemainingCount = Math.Max(0, listing.RemainingCount - step.Count);
        }
    }
}
