using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateSupplyContractForPool(string packId, NcSupplyContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[ContractsV2] Pack '{packId}' contains a supply contract with an empty prototype id.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(proto.Difficulty))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{proto.ID}' has empty difficulty. Contract skipped.");
            valid = false;
        }

        var requirements = GetSupplyRequirements(proto);
        if (requirements.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has no requirements. " +
                "Use 'requirements' with at least one entry. Contract skipped.");
            valid = false;
        }

        for (var i = 0; i < requirements.Count; i++)
        {
            if (!TryValidateSupplyRequirement(proto.ID, i, requirements[i]))
                valid = false;
        }

        if (!TryValidateSupplyRewardsForPool(proto))
            valid = false;

        return valid;
    }

    private bool TryValidateSupplyRequirement(
        string contractId,
        int index,
        NcSupplyRequirementEntry entry)
    {
        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
        var hasGroup = !string.IsNullOrWhiteSpace(entry.Group);

        if (hasPrototype == hasGroup)
        {
            Sawmill.Warning(
                hasPrototype
                    ? $"[ContractsV2] Supply contract '{contractId}' requirement #{index} has both prototype and group. Use exactly one."
                    : $"[ContractsV2] Supply contract '{contractId}' requirement #{index} has neither prototype nor group.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' requirement #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                return true;

            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' requirement #{index} references missing entity prototype " +
                $"'{entry.Prototype}'.");
            return false;
        }

        if (!_prototypes.TryIndex<NcItemGroupPrototype>(entry.Group, out var group))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' requirement #{index} references missing ncItemGroup " +
                $"'{entry.Group}'. Supply V2 group requirements must reference ncItemGroup prototypes, not legacy matchers.");
            return false;
        }

        if (!TryValidateItemGroup(contractId, entry.Group, group))
            return false;

        if (TryGetContractMatcherSpec(entry.Group, out _))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Supply contract '{contractId}' requirement #{index} references invalid item group '{entry.Group}'.");
        return false;
    }

    private bool TryValidateItemGroup(
        string ownerId,
        string groupId,
        NcItemGroupPrototype group)
    {
        var valid = true;
        var hasAnyEntry = false;

        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var prototypeId = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(prototypeId))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Item group '{groupId}' used by '{ownerId}' has empty prototypes[{i}].");
                valid = false;
                continue;
            }

            hasAnyEntry = true;
            if (_prototypes.HasIndex<EntityPrototype>(prototypeId))
                continue;

            Sawmill.Warning(
                $"[ContractsV2] Item group '{groupId}' used by '{ownerId}' references missing entity prototype " +
                $"'{prototypeId}'.");
            valid = false;
        }

        for (var i = 0; i < group.Tags.Count; i++)
        {
            var tag = group.Tags[i];
            if (string.IsNullOrWhiteSpace(tag))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Item group '{groupId}' used by '{ownerId}' has empty tags[{i}].");
                valid = false;
                continue;
            }

            hasAnyEntry = true;
        }

        if (hasAnyEntry)
            return valid;

        Sawmill.Warning(
            $"[ContractsV2] Item group '{groupId}' used by '{ownerId}' has no prototypes and no tags.");
        return false;
    }

    private bool TryValidateSupplyRewardsForPool(NcSupplyContractPrototype proto)
    {
        if (!HasSupplyRewards(proto.Rewards))
            return TryValidateLegacySupplyRewardForPool(proto);

        var valid = true;
        var hasAtLeastOneValidReward = false;

        if (proto.LegacyReward.Money > 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' uses both new 'rewards' block and legacy 'reward.money'. " +
                "Legacy reward.money will be ignored.");
        }

        for (var i = 0; i < proto.Rewards.Guaranteed.Count; i++)
        {
            if (TryValidateSupplyRewardEntry(proto.ID, $"rewards.guaranteed[{i}]", proto.Rewards.Guaranteed[i], 1.0f))
                hasAtLeastOneValidReward = true;
            else
                valid = false;
        }

        for (var i = 0; i < proto.Rewards.Random.Count; i++)
        {
            var reward = proto.Rewards.Random[i];
            if (TryValidateSupplyRewardEntry(proto.ID, $"rewards.random[{i}]", reward, reward.Chance))
                hasAtLeastOneValidReward = true;
            else
                valid = false;
        }

        for (var i = 0; i < proto.Rewards.Pools.Count; i++)
        {
            if (TryValidateSupplyRewardPoolRoll(proto.ID, $"rewards.pools[{i}]", proto.Rewards.Pools[i]))
                hasAtLeastOneValidReward = true;
            else
                valid = false;
        }

        if (hasAtLeastOneValidReward)
            return valid;

        Sawmill.Warning(
            $"[ContractsV2] Supply contract '{proto.ID}' has a rewards block, but no valid reward entries. Contract skipped.");
        return false;
    }

    private bool TryValidateLegacySupplyRewardForPool(NcSupplyContractPrototype proto)
    {
        if (proto.LegacyReward.Money > 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' uses legacy 'reward.money'. " +
                "Prefer rewards.guaranteed with type: Currency.");
            return true;
        }

        Sawmill.Warning(
            $"[ContractsV2] Supply contract '{proto.ID}' has no rewards. " +
            "Add rewards.guaranteed/random/pools or a temporary legacy reward.money. Contract skipped.");
        return false;
    }

    private bool TryValidateSupplyRewardEntry(
        string contractId,
        string path,
        NcSupplyRewardEntry entry,
        float chance)
    {
        if (!IsChanceValid(chance))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has invalid chance={chance}. Expected 0..1.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Amount))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' {path} has invalid amount range " +
                $"{entry.Amount.Min}..{entry.Amount.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{contractId}' {path} references missing entity prototype " +
                    $"'{entry.Prototype}'.");
                return false;

            case StoreRewardType.Currency:
                if (!string.IsNullOrWhiteSpace(entry.Currency))
                    return true;

                // Empty currency can still be valid if the store's contracts preset has skipCurrency.
                // That store-specific fallback is resolved later when the contract is actually generated.
                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{contractId}' {path} is Currency without explicit currency. " +
                    "It will require a store contracts preset skipCurrency fallback.");
                return true;

            case StoreRewardType.Pool:
                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{contractId}' {path} uses type: Pool inside guaranteed/random. " +
                    "Use rewards.pools instead.");
                return false;

            default:
                Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool TryValidateSupplyRewardPoolRoll(
        string contractId,
        string path,
        NcSupplyRewardPoolRollEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Pool))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has no pool id.");
            return false;
        }

        if (!_prototypes.TryIndex<NcContractRewardPoolPrototype>(entry.Pool, out var pool))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} references missing reward pool '{entry.Pool}'.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Rolls))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' {path} has invalid rolls range " +
                $"{entry.Rolls.Min}..{entry.Rolls.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        if (!IsChanceValid(entry.Chance))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has invalid chance={entry.Chance}. Expected 0..1.");
            return false;
        }

        return TryValidateRewardPoolPrototype(contractId, entry.Pool, pool);
    }

    private bool TryValidateRewardPoolPrototype(
        string ownerId,
        string poolId,
        NcContractRewardPoolPrototype pool)
    {
        if (pool.Entries.Count == 0)
        {
            Sawmill.Warning($"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' has no entries.");
            return false;
        }

        var valid = true;
        var hasAtLeastOneValidEntry = false;

        for (var i = 0; i < pool.Entries.Count; i++)
        {
            if (TryValidateRewardPoolEntry(ownerId, poolId, i, pool.Entries[i]))
                hasAtLeastOneValidEntry = true;
            else
                valid = false;
        }

        if (hasAtLeastOneValidEntry)
            return valid;

        Sawmill.Warning($"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' has no valid entries.");
        return false;
    }

    private bool TryValidateRewardPoolEntry(
        string ownerId,
        string poolId,
        int index,
        ContractRewardDef entry)
    {
        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} has non-positive weight={entry.Weight}.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Amount))
        {
            Sawmill.Warning(
                $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} has invalid amount range " +
                $"{entry.Amount.Min}..{entry.Amount.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        var probability = GetRewardProbability(entry);
        if (!IsChanceValid(probability))
        {
            Sawmill.Warning(
                $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} has invalid chance={probability}. Expected 0..1.");
            return false;
        }

        var rewardId = GetRewardId(entry);
        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(rewardId))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} is Item but has no prototype/id.");
                    return false;
                }

                if (_prototypes.HasIndex<EntityPrototype>(rewardId))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} references missing entity prototype " +
                    $"'{rewardId}'.");
                return false;

            case StoreRewardType.Currency:
                if (!string.IsNullOrWhiteSpace(rewardId))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} is Currency but has no currency/id.");
                return false;

            case StoreRewardType.Pool:
                if (string.IsNullOrWhiteSpace(rewardId))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} is Pool but has no pool/id.");
                    return false;
                }

                if (_prototypes.HasIndex<NcContractRewardPoolPrototype>(rewardId))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} references missing nested reward pool " +
                    $"'{rewardId}'.");
                return false;

            default:
                Sawmill.Warning(
                    $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} has unsupported reward type {entry.Type}.");
                return false;
        }
    }
}
