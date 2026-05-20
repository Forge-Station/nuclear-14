using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class StoreSystemStructuredLoader
{
    private bool ValidateBarterListing(
        NcBarterListingPrototype listingProto,
        ProtoId<NcBarterPresetPrototype> presetId,
        ProtoId<NcBarterCategoryPrototype> categoryId)
    {
        if (string.IsNullOrWhiteSpace(listingProto.ID))
        {
            Sawmill.Warning($"[NcStore] Barter entry in '{presetId}/{categoryId}' has empty id and was skipped.");
            return false;
        }

        if (listingProto.Cost.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{listingProto.ID}' has no cost and was skipped.");
            return false;
        }

        if (listingProto.Receive.Count == 0 && listingProto.ReceivePools.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{listingProto.ID}' has no receive or receivePools block and was skipped.");
            return false;
        }

        var ok = true;

        for (var i = 0; i < listingProto.Cost.Count; i++)
            ok &= ValidateBarterCost(listingProto.ID, $"cost[{i}]", listingProto.Cost[i]);

        for (var i = 0; i < listingProto.Receive.Count; i++)
            ok &= ValidateBarterReceive(listingProto.ID, $"receive[{i}]", listingProto.Receive[i]);

        for (var i = 0; i < listingProto.ReceivePools.Count; i++)
            ok &= ValidateBarterReceivePool(listingProto.ID, $"receivePools[{i}]", listingProto.ReceivePools[i]);

        if (listingProto.Count == 0 || listingProto.Count < -1)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{listingProto.ID}' has invalid count={listingProto.Count}. Use -1 or a positive value.");
            ok = false;
        }

        return ok;
    }

    private bool ValidateBarterCost(string entryId, string path, NcBarterCostEntry cost)
    {
        var sources = CountNonEmpty(cost.Prototype, cost.Group, cost.TagTarget, cost.Currency);
        if (sources != 1)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} must specify exactly one of prototype/group/tagTarget/currency.");
            return false;
        }

        if (cost.Count <= 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has non-positive count={cost.Count}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(cost.Prototype) && !_prototypes.HasIndex<EntityPrototype>(cost.Prototype))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing entity prototype '{cost.Prototype}'.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(cost.Group) && !ValidateBarterItemGroup(entryId, path, cost.Group))
            return false;

        if (!string.IsNullOrWhiteSpace(cost.TagTarget) && !ValidateTradeTagTarget(entryId, path, cost.TagTarget))
            return false;

        if (!string.IsNullOrWhiteSpace(cost.Currency) && !_prototypes.HasIndex<StackPrototype>(cost.Currency))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing stack currency '{cost.Currency}'.");
            return false;
        }

        return true;
    }

    private bool ValidateBarterReceive(string entryId, string path, NcBarterReceiveEntry receive)
    {
        var sources = CountNonEmpty(receive.Prototype, receive.Currency);
        if (sources != 1)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} must specify exactly one of prototype/currency.");
            return false;
        }

        if (receive.Count <= 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has non-positive count={receive.Count}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(receive.Prototype) && !_prototypes.HasIndex<EntityPrototype>(receive.Prototype))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing entity prototype '{receive.Prototype}'.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(receive.Currency) && !_prototypes.HasIndex<StackPrototype>(receive.Currency))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing stack currency '{receive.Currency}'.");
            return false;
        }

        return true;
    }

    private bool ValidateBarterReceivePool(string entryId, string path, NcBarterReceivePoolEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Pool))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has empty pool id.");
            return false;
        }

        if (entry.Rolls.Min <= 0 || entry.Rolls.Max <= 0 || entry.Rolls.Min > entry.Rolls.Max)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} has invalid rolls range " +
                $"{entry.Rolls.Min}..{entry.Rolls.Max}.");
            return false;
        }

        if (entry.Chance < 0f || entry.Chance > 1f)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has invalid chance={entry.Chance}. Expected 0..1.");
            return false;
        }

        if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(entry.Pool, out var pool) || pool.Entries.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing or empty reward pool '{entry.Pool}'.");
            return false;
        }

        var ok = true;
        var visited = new HashSet<string>(StringComparer.Ordinal) { entry.Pool };
        for (var i = 0; i < pool.Entries.Count; i++)
            ok &= ValidateBarterReceivePoolReward(entryId, $"{path}.pool[{i}]", pool.Entries[i], visited);

        if (ok)
            return true;

        Sawmill.Warning(
            $"[NcStore] Barter entry '{entryId}' {path} pool '{entry.Pool}' contains entries that are not valid for barter. " +
            "Only Item, Currency and acyclic nested Pool rewards are supported.");
        return false;
    }

    private bool ValidateBarterReceivePoolReward(
        string entryId,
        string path,
        NcSupplyRewardPoolEntry reward,
        HashSet<string> visited)
    {
        if (reward.Type != StoreRewardType.Item &&
            reward.Type != StoreRewardType.Currency &&
            reward.Type != StoreRewardType.Pool)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} must be Item, Currency or Pool.");
            return false;
        }

        if (reward.Weight <= 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has non-positive weight={reward.Weight}.");
            return false;
        }

        if (reward.Count.Min < 0 || reward.Count.Max <= 0 || reward.Count.Min > reward.Count.Max)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} has invalid count range " +
                $"{reward.Count.Min}..{reward.Count.Max}.");
            return false;
        }

        if (reward.MaxRepeats < 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has invalid max={reward.MaxRepeats}.");
            return false;
        }

        if (reward.Type == StoreRewardType.Item)
        {
            if (string.IsNullOrWhiteSpace(reward.Prototype))
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has empty item prototype.");
                return false;
            }

            if (!_prototypes.HasIndex<EntityPrototype>(reward.Prototype))
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing entity prototype '{reward.Prototype}'.");
                return false;
            }
        }

        if (reward.Type == StoreRewardType.Currency)
        {
            if (string.IsNullOrWhiteSpace(reward.Currency))
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has empty currency.");
                return false;
            }

            if (!_prototypes.HasIndex<StackPrototype>(reward.Currency))
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing stack currency '{reward.Currency}'.");
                return false;
            }
        }

        if (reward.Type == StoreRewardType.Pool)
        {
            if (string.IsNullOrWhiteSpace(reward.Pool))
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has empty nested pool id.");
                return false;
            }

            if (!visited.Add(reward.Pool))
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} creates a nested reward pool cycle.");
                return false;
            }

            if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(reward.Pool, out var nestedPool) ||
                nestedPool.Entries.Count == 0)
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing or empty nested reward pool '{reward.Pool}'.");
                visited.Remove(reward.Pool);
                return false;
            }

            var ok = true;
            for (var i = 0; i < nestedPool.Entries.Count; i++)
                ok &= ValidateBarterReceivePoolReward(entryId, $"{path}.pool[{i}]", nestedPool.Entries[i], visited);

            visited.Remove(reward.Pool);
            return ok;
        }

        return true;
    }

    private bool ValidateBarterItemGroup(string entryId, string path, string groupId)
    {
        if (!_prototypes.TryIndex<NcItemGroupPrototype>(groupId, out var group))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing item group '{groupId}'.");
            return false;
        }

        if (group.Prototypes.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references empty item group '{groupId}'.");
            return false;
        }

        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var protoId = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(protoId) || !_prototypes.HasIndex<EntityPrototype>(protoId))
            {
                Sawmill.Warning(
                    $"[NcStore] Barter entry '{entryId}' {path} item group '{groupId}' has invalid prototype '{protoId}'.");
                return false;
            }
        }

        return true;
    }
}
