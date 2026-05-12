using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public int GetMaxBarterCountFromSnapshot(NcStoreListingDef listing, in NcInventorySnapshot snapshot)
    {
        if (listing.Mode != StoreMode.Exchange || listing.BarterCost.Count == 0 || listing.BarterReceive.Count == 0)
            return 0;

        var max = int.MaxValue;

        for (var i = 0; i < listing.BarterCost.Count; i++)
        {
            var cost = listing.BarterCost[i];
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

    public bool TryBarter(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
                NcStoreComponent.MakeListingKey(StoreMode.Exchange, listingId),
                out var listing))
            return false;

        if (listing.BarterCost.Count == 0 || listing.BarterReceive.Count == 0)
            return false;

        if (!ValidateBarterReceivePrototypes(listing))
            return false;

        _inventory.InvalidateInventoryCache(user);
        var snapshot = _inventory.BuildInventorySnapshot(user);
        var maxPossible = GetMaxBarterCountFromSnapshot(listing, snapshot);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);
        if (actual <= 0)
            return false;

        if (!TryTakeBarterCost(user, listing, actual))
            return false;

        if (!TryGiveBarterReceive(user, listing, actual))
        {
            Sawmill.Warning(
                $"[NcStore] Barter '{listing.Id}' consumed cost but failed to give all receive entries. " +
                "Check receive prototypes/currencies.");
            _inventory.InvalidateInventoryCache(user);
            return false;
        }

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - actual);

        _inventory.InvalidateInventoryCache(user);
        Sawmill.Info($"TryBarter: OK listing='{listing.Id}' x{actual}");
        return true;
    }

    private bool TryGetAffordableBarterUnitsFromSnapshot(
        NcBarterCostEntry cost,
        in NcInventorySnapshot snapshot,
        out int possible)
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

    private bool TryTakeBarterCost(EntityUid user, NcStoreListingDef listing, int times)
    {
        for (var i = 0; i < listing.BarterCost.Count; i++)
        {
            var cost = listing.BarterCost[i];
            if (!TryMultiplyPositive(cost.Count, times, out var amount))
                return false;

            if (!string.IsNullOrWhiteSpace(cost.Currency))
            {
                if (!TryTakeCurrency(user, cost.Currency, amount))
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Prototype))
            {
                if (!_inventory.TryTakeProductUnitsFromRootCached(user, cost.Prototype, amount, PrototypeMatchMode.Exact))
                    return false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cost.Group))
            {
                if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                    return false;

                if (!_inventory.TryTakeItemGroupUnitsFromRootCached(user, group, amount))
                    return false;
                continue;
            }

            return false;
        }

        return true;
    }

    private bool TryGiveBarterReceive(EntityUid user, NcStoreListingDef listing, int times)
    {
        for (var i = 0; i < listing.BarterReceive.Count; i++)
        {
            var receive = listing.BarterReceive[i];
            if (!TryMultiplyPositive(receive.Count, times, out var amount))
                return false;

            if (!string.IsNullOrWhiteSpace(receive.Currency))
            {
                GiveCurrency(user, receive.Currency, amount);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(receive.Prototype))
            {
                var spawned = TrySpawnProductUnits(receive.Prototype, user, amount);
                if (spawned < amount)
                    return false;
                continue;
            }

            return false;
        }

        return true;
    }

    private bool ValidateBarterReceivePrototypes(NcStoreListingDef listing)
    {
        for (var i = 0; i < listing.BarterReceive.Count; i++)
        {
            var receive = listing.BarterReceive[i];
            if (!string.IsNullOrWhiteSpace(receive.Currency))
                continue;

            if (string.IsNullOrWhiteSpace(receive.Prototype) || !_protos.HasIndex<EntityPrototype>(receive.Prototype))
                return false;
        }

        return true;
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
}
