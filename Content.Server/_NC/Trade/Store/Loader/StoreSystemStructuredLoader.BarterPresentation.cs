using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class StoreSystemStructuredLoader
{
    private string ResolveBarterIcon(NcBarterListingPrototype listingProto)
    {
        if (!string.IsNullOrWhiteSpace(listingProto.Icon) && _prototypes.HasIndex<EntityPrototype>(listingProto.Icon))
            return listingProto.Icon;

        foreach (var receive in listingProto.Receive)
        {
            if (!string.IsNullOrWhiteSpace(receive.Prototype))
                return receive.Prototype;

            if (!string.IsNullOrWhiteSpace(receive.Currency) && TryResolveCurrencyIcon(receive.Currency, out var currencyIcon))
                return currencyIcon;
        }

        foreach (var pool in listingProto.ReceivePools)
        {
            if (TryResolveRewardPoolIcon(pool.Pool, out var poolIcon))
                return poolIcon;
        }

        foreach (var cost in listingProto.Cost)
        {
            if (!string.IsNullOrWhiteSpace(cost.Prototype))
                return cost.Prototype;

            if (!string.IsNullOrWhiteSpace(cost.Group) &&
                _prototypes.TryIndex<NcItemGroupPrototype>(cost.Group, out var group) &&
                !string.IsNullOrWhiteSpace(group.Icon) &&
                _prototypes.HasIndex<EntityPrototype>(group.Icon))
                return group.Icon;

            if (!string.IsNullOrWhiteSpace(cost.TagTarget) &&
                _prototypes.TryIndex<NcTradeTagPrototype>(cost.TagTarget, out var tagTarget) &&
                !string.IsNullOrWhiteSpace(tagTarget.Icon) &&
                _prototypes.HasIndex<EntityPrototype>(tagTarget.Icon))
                return tagTarget.Icon;

            if (!string.IsNullOrWhiteSpace(cost.Currency) && TryResolveCurrencyIcon(cost.Currency, out var currencyIcon))
                return currencyIcon;
        }

        return string.Empty;
    }

    private bool TryResolveRewardPoolIcon(
        string poolId,
        out string icon,
        HashSet<string> visited,
        int depth)
    {
        icon = string.Empty;
        if (string.IsNullOrWhiteSpace(poolId) || depth > MaxRewardPoolTraversalDepth)
            return false;

        if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(poolId, out var supplyPool))
            return false;

        if (!visited.Add(poolId))
            return false;

        for (var i = 0; i < supplyPool.Entries.Count; i++)
        {
            var reward = supplyPool.Entries[i];

            if (reward.Type == StoreRewardType.Item &&
                !string.IsNullOrWhiteSpace(reward.Prototype) &&
                _prototypes.HasIndex<EntityPrototype>(reward.Prototype))
            {
                icon = reward.Prototype;
                visited.Remove(poolId);
                return true;
            }

            if (reward.Type == StoreRewardType.Currency &&
                !string.IsNullOrWhiteSpace(reward.Currency) &&
                TryResolveCurrencyIcon(reward.Currency, out icon))
            {
                visited.Remove(poolId);
                return true;
            }

            if (reward.Type == StoreRewardType.Pool &&
                TryResolveRewardPoolIcon(reward.Pool, out icon, visited, depth + 1))
            {
                visited.Remove(poolId);
                return true;
            }
        }

        visited.Remove(poolId);
        return false;
    }

    private bool TryResolveRewardPoolIcon(string poolId, out string icon)
    {
        return TryResolveRewardPoolIcon(poolId, out icon, new HashSet<string>(StringComparer.Ordinal), 0);
    }

    private bool TryResolveCurrencyIcon(string currency, out string icon)
    {
        icon = string.Empty;
        if (!_prototypes.TryIndex<StackPrototype>(currency, out var stack) || string.IsNullOrWhiteSpace(stack.Spawn))
            return false;

        if (!_prototypes.HasIndex<EntityPrototype>(stack.Spawn))
            return false;

        icon = stack.Spawn;
        return true;
    }

    private static List<NcBarterCostEntry> CloneBarterCost(List<NcBarterCostEntry> source)
    {
        var result = new List<NcBarterCostEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var c = source[i];
            result.Add(new NcBarterCostEntry
            {
                Prototype = c.Prototype,
                Group = c.Group,
                TagTarget = c.TagTarget,
                Currency = c.Currency,
                Count = c.Count
            });
        }

        return result;
    }

    private static List<NcBarterReceiveEntry> CloneBarterReceive(List<NcBarterReceiveEntry> source)
    {
        var result = new List<NcBarterReceiveEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var r = source[i];
            result.Add(new NcBarterReceiveEntry
            {
                Prototype = r.Prototype,
                Currency = r.Currency,
                Count = r.Count
            });
        }

        return result;
    }

    private static List<NcBarterReceivePoolEntry> CloneBarterReceivePools(List<NcBarterReceivePoolEntry> source)
    {
        var result = new List<NcBarterReceivePoolEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var r = source[i];
            result.Add(new NcBarterReceivePoolEntry
            {
                Pool = r.Pool,
                Rolls = r.Rolls,
                Chance = r.Chance
            });
        }

        return result;
    }
}
