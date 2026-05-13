using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
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

        if (proto.Targets.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has no targets. " +
                "Use 'targets' with at least one entry. Contract skipped.");
            valid = false;
        }

        if (!TryValidateSupplyTargetCount(proto))
            valid = false;

        for (var i = 0; i < proto.Targets.Count; i++)
        {
            if (!TryValidateSupplyTarget(proto.ID, i, proto.Targets[i]))
                valid = false;
        }

        if (!TryValidateSupplyRewardsForPool(proto))
            valid = false;

        return valid;
    }

    private bool TryValidateSupplyTargetCount(NcSupplyContractPrototype proto)
    {
        if (!IsSupplyTargetCountConfigured(proto.TargetCount))
            return true;

        var range = proto.TargetCount;
        if (range.Min < 1 || range.Max < 1 || range.Min > range.Max)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has invalid targetCount range " +
                $"{range.Min}..{range.Max}. Expected min >= 1, max >= min.");
            return false;
        }

        if (proto.Targets.Count > 0 && range.Max > proto.Targets.Count)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has targetCount max={range.Max}, " +
                $"but only {proto.Targets.Count} targets are defined.");
            return false;
        }

        return true;
    }

    private bool TryValidateSupplyTarget(
        string contractId,
        int index,
        NcSupplyTargetEntry entry)
    {
        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
        var hasGroup = !string.IsNullOrWhiteSpace(entry.Group);

        if (hasPrototype == hasGroup)
        {
            Sawmill.Warning(
                hasPrototype
                    ? $"[ContractsV2] Supply contract '{contractId}' target #{index} has both prototype and group. Use exactly one."
                    : $"[ContractsV2] Supply contract '{contractId}' target #{index} has neither prototype nor group.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' target #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' target #{index} has non-positive weight={entry.Weight}. " +
                "Weight is used when targetCount is configured and must be > 0.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                return true;

            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' target #{index} references missing entity prototype " +
                $"'{entry.Prototype}'.");
            return false;
        }

        if (!_prototypes.TryIndex<NcItemGroupPrototype>(entry.Group, out var group))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' target #{index} references missing ncItemGroup " +
                $"'{entry.Group}'. Supply V2 group targets must reference ncItemGroup prototypes, not legacy matchers.");
            return false;
        }

        if (!TryValidateItemGroup(contractId, entry.Group, group))
            return false;

        if (TryGetContractMatcherSpec(entry.Group, out _))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Supply contract '{contractId}' target #{index} references invalid item group '{entry.Group}'.");
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
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has no reward entries. " +
                "Use 'reward' as a list with type: Currency, Item or Pool. Contract skipped.");
            return false;
        }

        var valid = true;
        var hasAtLeastOneValidReward = false;

        for (var i = 0; i < proto.Reward.Count; i++)
        {
            if (TryValidateSupplyRewardEntry(proto.ID, $"reward[{i}]", proto.Reward[i]))
                hasAtLeastOneValidReward = true;
            else
                valid = false;
        }

        if (hasAtLeastOneValidReward)
            return valid;

        Sawmill.Warning(
            $"[ContractsV2] Supply contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }

    private bool TryValidateSupplyRewardEntry(
        string contractId,
        string path,
        NcSupplyRewardEntry entry)
    {
        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min >= 0, max > 0, min <= max.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (!RequireOnlyRewardTarget(contractId, path, nameof(entry.Prototype), entry.Prototype, entry.Currency, entry.Pool))
                    return false;

                if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{contractId}' {path} references missing entity prototype " +
                    $"'{entry.Prototype}'.");
                return false;

            case StoreRewardType.Currency:
                if (!RequireOnlyRewardTarget(contractId, path, nameof(entry.Currency), entry.Currency, entry.Prototype, entry.Pool))
                    return false;

                if (_prototypes.HasIndex<StackPrototype>(entry.Currency))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{contractId}' {path} references missing stack currency " +
                    $"'{entry.Currency}'.");
                return false;

            case StoreRewardType.Pool:
                if (!RequireOnlyRewardTarget(contractId, path, nameof(entry.Pool), entry.Pool, entry.Prototype, entry.Currency))
                    return false;

                if (!_prototypes.TryIndex<NcContractRewardPoolPrototype>(entry.Pool, out var pool))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Supply contract '{contractId}' {path} references missing reward pool '{entry.Pool}'.");
                    return false;
                }

                return TryValidateRewardPoolPrototype(contractId, entry.Pool, pool);

            default:
                Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool RequireOnlyRewardTarget(
        string contractId,
        string path,
        string expectedField,
        string expectedValue,
        string otherA,
        string otherB)
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} requires field '{expectedField}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(otherA) && string.IsNullOrWhiteSpace(otherB))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Supply contract '{contractId}' {path} has extra reward target fields. " +
            $"For each reward entry use only the field required by its type.");
        return false;
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

        var amountRange = GetRewardAmountRange(entry);
        if (!IsRewardCountRange(amountRange))
        {
            Sawmill.Warning(
                $"[ContractsV2] Reward pool '{poolId}' used by '{ownerId}' entry #{index} has invalid count/amount range " +
                $"{amountRange.Min}..{amountRange.Max}. Expected min >= 0, max > 0, min <= max.");
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

    private static bool IsRewardCountRange(IntRange range)
    {
        return range.Min >= 0 && range.Max > 0 && range.Min <= range.Max;
    }

    private static bool IsChanceValid(float chance)
    {
        return chance >= 0f && chance <= 1f;
    }

}
