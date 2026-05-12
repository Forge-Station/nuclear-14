using System.Linq;
using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    [Dependency] private readonly TagSystem _tags = default!;

    private sealed class BarterCostPlan
    {
        public readonly List<BarterCostReservation> Reservations = new();
        public int Count;
    }

    private readonly record struct BarterCostReservation(EntityUid Entity, int Amount);

    public int GetMaxBarterCountFromSnapshot(NcStoreListingDef listing, in NcInventorySnapshot snapshot)
    {
        if (listing.Mode != StoreMode.Barter || listing.BarterCost.Count == 0 || listing.BarterReceive.Count == 0)
            return 0;

        var upper = EstimateBarterUpperBoundFromSnapshot(listing, snapshot);
        if (listing.RemainingCount >= 0)
            upper = Math.Min(upper, listing.RemainingCount);

        if (upper <= 0)
            return 0;

        if (CanAffordBarterFromSnapshot(listing, snapshot, upper))
            return upper;

        var low = 0;
        var high = upper;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (CanAffordBarterFromSnapshot(listing, snapshot, mid))
                low = mid;
            else
                high = mid - 1;
        }

        return Math.Max(0, low);
    }

    public bool TryBarter(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
                NcStoreComponent.MakeListingKey(StoreMode.Barter, listingId),
                out var listing))
            return false;

        if (listing.BarterCost.Count == 0 || listing.BarterReceive.Count == 0)
            return false;

        if (!ValidateBarterReceivePrototypes(listing))
            return false;

        var requested = count;
        if (listing.RemainingCount >= 0)
            requested = Math.Min(requested, listing.RemainingCount);

        if (requested <= 0)
            return false;

        _inventory.InvalidateInventoryCache(user);

        var cachedItems = new List<EntityUid>();
        _inventory.ScanInventoryItems(user, cachedItems);

        if (!TryFindBestBarterCostPlan(user, listing, cachedItems, requested, out var plan))
            return false;

        if (plan.Count <= 0)
            return false;

        if (!ExecuteBarterCostPlan(plan))
        {
            Sawmill.Warning($"[NcStore] Barter '{listing.Id}' cost plan failed during execution; no receive was given.");
            _inventory.InvalidateInventoryCache(user);
            return false;
        }

        if (!TryGiveBarterReceive(user, listing, plan.Count))
        {
            Sawmill.Warning(
                $"[NcStore] Barter '{listing.Id}' consumed cost but failed to give all receive entries. " +
                "Check receive prototypes/currencies. Cost rollback is not available after physical item deletion.");
            _inventory.InvalidateInventoryCache(user);
            return false;
        }

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - plan.Count);

        _inventory.InvalidateInventoryCache(user);
        Sawmill.Info($"TryBarter: OK listing='{listing.Id}' x{plan.Count}");
        return true;
    }

    private int EstimateBarterUpperBoundFromSnapshot(NcStoreListingDef listing, in NcInventorySnapshot snapshot)
    {
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

        return Math.Max(0, max);
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

    private bool CanAffordBarterFromSnapshot(NcStoreListingDef listing, in NcInventorySnapshot snapshot, int times)
    {
        if (times <= 0)
            return true;

        var protoCounts = new Dictionary<string, int>(snapshot.ProtoCounts, StringComparer.Ordinal);
        var stackCounts = new Dictionary<string, int>(snapshot.StackTypeCounts, StringComparer.Ordinal);

        for (var i = 0; i < listing.BarterCost.Count; i++)
        {
            var cost = listing.BarterCost[i];
            if (!TryMultiplyPositive(cost.Count, times, out var amount))
                return false;

            if (!TryConsumeBarterCostFromWorkingSnapshot(cost, amount, protoCounts, stackCounts))
                return false;
        }

        return true;
    }

    private bool TryConsumeBarterCostFromWorkingSnapshot(
        NcBarterCostEntry cost,
        int amount,
        Dictionary<string, int> protoCounts,
        Dictionary<string, int> stackCounts)
    {
        if (amount <= 0)
            return true;

        if (!string.IsNullOrWhiteSpace(cost.Currency))
            return TryConsumeWorkingCount(stackCounts, cost.Currency, amount);

        if (!string.IsNullOrWhiteSpace(cost.Prototype))
        {
            var stackType = _inventory.GetProductStackType(cost.Prototype);
            if (!string.IsNullOrWhiteSpace(stackType))
            {
                if (!TryConsumeWorkingCount(stackCounts, stackType, amount))
                    return false;

                // Keep prototype-based group previews conservative when the stack prototype is also present in ProtoCounts.
                TryConsumeWorkingCount(protoCounts, cost.Prototype, Math.Min(amount, protoCounts.TryGetValue(cost.Prototype, out var protoHave) ? protoHave : 0));
                return true;
            }

            return TryConsumeWorkingCount(protoCounts, cost.Prototype, amount);
        }

        if (!string.IsNullOrWhiteSpace(cost.Group))
        {
            if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                return false;

            return TryConsumeGroupFromWorkingSnapshot(group, amount, protoCounts);
        }

        return false;
    }

    private bool TryConsumeGroupFromWorkingSnapshot(
        NcItemGroupPrototype group,
        int amount,
        Dictionary<string, int> protoCounts)
    {
        var left = amount;

        var explicitPrototypes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var protoId = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(protoId) || !explicitPrototypes.Add(protoId))
                continue;

            ConsumeMatchingWorkingProto(protoId, protoCounts, ref left);
            if (left <= 0)
                return true;
        }

        if (group.Tags.Count == 0)
            return false;

        foreach (var protoId in protoCounts.Keys.ToArray())
        {
            if (left <= 0)
                return true;

            if (explicitPrototypes.Contains(protoId))
                continue;

            if (!PrototypeHasAnyItemGroupTag(protoId, group))
                continue;

            ConsumeMatchingWorkingProto(protoId, protoCounts, ref left);
        }

        return left <= 0;
    }

    private static void ConsumeMatchingWorkingProto(string protoId, Dictionary<string, int> protoCounts, ref int left)
    {
        if (left <= 0)
            return;

        if (!protoCounts.TryGetValue(protoId, out var have) || have <= 0)
            return;

        var take = Math.Min(have, left);
        protoCounts[protoId] = have - take;
        left -= take;
    }

    private static bool TryConsumeWorkingCount(Dictionary<string, int> counts, string id, int amount)
    {
        if (!counts.TryGetValue(id, out var have) || have < amount)
            return false;

        counts[id] = have - amount;
        return true;
    }

    private bool TryFindBestBarterCostPlan(
        EntityUid user,
        NcStoreListingDef listing,
        IReadOnlyList<EntityUid> cachedItems,
        int requested,
        out BarterCostPlan plan)
    {
        plan = new BarterCostPlan();

        if (requested <= 0)
            return false;

        if (TryBuildBarterCostPlan(user, listing, cachedItems, requested, out plan))
            return true;

        var low = 0;
        var high = requested - 1;
        BarterCostPlan? best = null;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (TryBuildBarterCostPlan(user, listing, cachedItems, mid, out var midPlan))
            {
                low = mid;
                best = midPlan;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (best != null && best.Count == low && low > 0)
        {
            plan = best;
            return true;
        }

        if (low > 0 && TryBuildBarterCostPlan(user, listing, cachedItems, low, out plan))
            return true;

        plan = new BarterCostPlan();
        return false;
    }

    private bool TryBuildBarterCostPlan(
        EntityUid user,
        NcStoreListingDef listing,
        IReadOnlyList<EntityUid> cachedItems,
        int times,
        out BarterCostPlan plan)
    {
        plan = new BarterCostPlan { Count = times };

        if (times <= 0)
            return false;

        var reservedUnits = new Dictionary<EntityUid, int>();

        for (var i = 0; i < listing.BarterCost.Count; i++)
        {
            var cost = listing.BarterCost[i];
            if (!TryMultiplyPositive(cost.Count, times, out var amount))
                return false;

            if (!TryReserveBarterCost(user, cachedItems, reservedUnits, cost, amount, plan))
                return false;
        }

        return true;
    }

    private bool TryReserveBarterCost(
        EntityUid user,
        IReadOnlyList<EntityUid> cachedItems,
        Dictionary<EntityUid, int> reservedUnits,
        NcBarterCostEntry cost,
        int amount,
        BarterCostPlan plan)
    {
        if (amount <= 0)
            return true;

        if (!string.IsNullOrWhiteSpace(cost.Currency))
            return TryReserveStackTypeUnits(user, cachedItems, reservedUnits, cost.Currency, amount, plan);

        if (!string.IsNullOrWhiteSpace(cost.Prototype))
        {
            var stackType = _inventory.GetProductStackType(cost.Prototype);
            if (!string.IsNullOrWhiteSpace(stackType))
                return TryReserveStackTypeUnits(user, cachedItems, reservedUnits, stackType, amount, plan);

            return TryReservePrototypeUnits(user, cachedItems, reservedUnits, cost.Prototype, amount, plan);
        }

        if (!string.IsNullOrWhiteSpace(cost.Group))
        {
            if (!_protos.TryIndex<NcItemGroupPrototype>(cost.Group, out var group))
                return false;

            return TryReserveItemGroupUnits(user, cachedItems, reservedUnits, group, amount, plan);
        }

        return false;
    }

    private bool TryReserveStackTypeUnits(
        EntityUid user,
        IReadOnlyList<EntityUid> cachedItems,
        Dictionary<EntityUid, int> reservedUnits,
        string stackType,
        int amount,
        BarterCostPlan plan)
    {
        var left = amount;

        for (var i = 0; i < cachedItems.Count && left > 0; i++)
        {
            var ent = cachedItems[i];
            if (ShouldSkipBarterCostEntity(user, ent))
                continue;

            if (!TryComp(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
                continue;

            ReserveUnitsFromEntity(ent, Math.Max(0, stack.Count), reservedUnits, plan, ref left);
        }

        return left <= 0;
    }

    private bool TryReservePrototypeUnits(
        EntityUid user,
        IReadOnlyList<EntityUid> cachedItems,
        Dictionary<EntityUid, int> reservedUnits,
        string protoId,
        int amount,
        BarterCostPlan plan)
    {
        var left = amount;

        for (var i = 0; i < cachedItems.Count && left > 0; i++)
        {
            var ent = cachedItems[i];
            if (ShouldSkipBarterCostEntity(user, ent))
                continue;

            if (!TryComp(ent, out MetaDataComponent? meta) || meta.EntityPrototype?.ID != protoId)
                continue;

            var available = CountBarterReservableUnits(ent);
            ReserveUnitsFromEntity(ent, available, reservedUnits, plan, ref left);
        }

        return left <= 0;
    }

    private bool TryReserveItemGroupUnits(
        EntityUid user,
        IReadOnlyList<EntityUid> cachedItems,
        Dictionary<EntityUid, int> reservedUnits,
        NcItemGroupPrototype group,
        int amount,
        BarterCostPlan plan)
    {
        var left = amount;

        for (var i = 0; i < cachedItems.Count && left > 0; i++)
        {
            var ent = cachedItems[i];
            if (ShouldSkipBarterCostEntity(user, ent))
                continue;

            if (!EntityMatchesItemGroup(ent, group))
                continue;

            var available = CountBarterReservableUnits(ent);
            ReserveUnitsFromEntity(ent, available, reservedUnits, plan, ref left);
        }

        return left <= 0;
    }

    private bool ShouldSkipBarterCostEntity(EntityUid user, EntityUid ent)
    {
        return ent == EntityUid.Invalid || !_ents.EntityExists(ent) || _inventory.IsProtectedFromDirectSale(user, ent);
    }

    private int CountBarterReservableUnits(EntityUid ent)
    {
        if (TryComp(ent, out StackComponent? stack))
            return Math.Max(0, stack.Count);

        return 1;
    }

    private void ReserveUnitsFromEntity(
        EntityUid ent,
        int availableTotal,
        Dictionary<EntityUid, int> reservedUnits,
        BarterCostPlan plan,
        ref int left)
    {
        if (left <= 0 || availableTotal <= 0)
            return;

        reservedUnits.TryGetValue(ent, out var alreadyReserved);
        var available = Math.Max(0, availableTotal - alreadyReserved);
        if (available <= 0)
            return;

        var take = Math.Min(available, left);
        reservedUnits[ent] = alreadyReserved + take;
        plan.Reservations.Add(new BarterCostReservation(ent, take));
        left -= take;
    }

    private bool EntityMatchesItemGroup(EntityUid ent, NcItemGroupPrototype group)
    {
        if (!TryComp(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return false;

        var protoId = meta.EntityPrototype.ID;
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            if (string.Equals(group.Prototypes[i], protoId, StringComparison.Ordinal))
                return true;
        }

        if (group.Tags.Count == 0)
            return false;

        if (!TryComp<TagComponent>(ent, out var tagComponent))
            return false;

        for (var i = 0; i < group.Tags.Count; i++)
        {
            if (_tags.HasTag(tagComponent, group.Tags[i]))
                return true;
        }

        return false;
    }

    private bool PrototypeHasAnyItemGroupTag(string protoId, NcItemGroupPrototype group)
    {
        if (group.Tags.Count == 0)
            return false;

        if (!_protos.TryIndex<EntityPrototype>(protoId, out var proto))
            return false;

        if (!proto.TryGetComponent(out TagComponent? tagComponent, _compFactory) || tagComponent == null)
            return false;

        for (var i = 0; i < group.Tags.Count; i++)
        {
            if (_tags.HasTag(tagComponent, group.Tags[i]))
                return true;
        }

        return false;
    }

    private bool ExecuteBarterCostPlan(BarterCostPlan plan)
    {
        if (plan.Reservations.Count == 0)
            return false;

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            var reservation = plan.Reservations[i];
            if (!TryConsumeBarterReservation(reservation))
                return false;
        }

        return true;
    }

    private bool TryConsumeBarterReservation(BarterCostReservation reservation)
    {
        if (reservation.Amount <= 0 || !_ents.EntityExists(reservation.Entity))
            return false;

        if (TryComp(reservation.Entity, out StackComponent? stack))
        {
            var have = Math.Max(0, stack.Count);
            if (have < reservation.Amount)
                return false;

            _stacks.SetCount(reservation.Entity, have - reservation.Amount, stack);
            if (stack.Count <= 0 && _ents.EntityExists(reservation.Entity))
                _ents.DeleteEntity(reservation.Entity);

            return true;
        }

        if (reservation.Amount != 1)
            return false;

        _ents.DeleteEntity(reservation.Entity);
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
