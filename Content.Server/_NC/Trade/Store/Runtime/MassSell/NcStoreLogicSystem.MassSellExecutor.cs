using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    // ==============================================================
    // Mass Sell – Execution (world mutations)
    // ==============================================================

    public bool TryMassSellFromContainer(
        EntityUid machine,
        NcStoreComponent store,
        EntityUid user,
        EntityUid container
    )
    {
        if (store.Listings.Count == 0)
            return false;

        InvalidateInventoryCache(container);

        _scratchItems.Clear();
        var cached = GetOrBuildDeepItemsCacheCompacted(container);

        foreach (var item in cached)
            _scratchItems.Add(item);

        var cachedItems = _scratchItems;

        var plan = ComputeMassSellPlanFromCachedItems(store, container, cachedItems);
        if (plan.Steps.Count == 0 || plan.IncomeByCurrency.Count == 0)
            return false;

        var incomeActual = new Dictionary<string, int>();
        var any = false;

        foreach (var step in plan.Steps)
        {
            if (step.Count <= 0 ||
                step.UnitPrice <= 0 ||
                string.IsNullOrWhiteSpace(step.CurrencyId) ||
                string.IsNullOrWhiteSpace(step.Listing.ProductEntity))
                continue;

            var listing = step.Listing;

            var remaining = listing.RemainingCount;
            if (remaining < -1)
                remaining = -1;

            var maxByRemaining = remaining >= 0 ? remaining : int.MaxValue;
            if (maxByRemaining <= 0)
                continue;

            var take = Math.Min(step.Count, maxByRemaining);
            if (take <= 0)
                continue;

            if (!TryTakeProductUnitsFromCachedList(
                container,
                cachedItems,
                listing.ProductEntity,
                take,
                listing.MatchMode))
                continue;

            if (listing.RemainingCount > 0)
                listing.RemainingCount = Math.Max(0, listing.RemainingCount - take);

            var total = (long) step.UnitPrice * take;
            SafeAddIncome(incomeActual, step.CurrencyId, total);

            any = true;
        }

        if (!any || incomeActual.Count == 0)
            return false;

        foreach (var (currency, amount) in incomeActual)
        {
            if (amount <= 0)
                continue;

            GiveCurrency(user, currency, amount);
        }

        InvalidateInventoryCache(container);
        InvalidateInventoryCache(user);

        return true;
    }
}
