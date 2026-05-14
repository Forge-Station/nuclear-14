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

    private bool TryValidateRetrievalContractForPool(string packId, NcRetrievalContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[ContractsV2] Pack '{packId}' contains a retrieval contract with an empty prototype id.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(proto.Difficulty))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{proto.ID}' has empty difficulty. Contract skipped.");
            valid = false;
        }

        if (proto.Targets.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has no targets. " +
                "Use 'targets' with at least one entry. Contract skipped.");
            valid = false;
        }

        if (!TryValidateRetrievalTargetCount(proto))
            valid = false;

        for (var i = 0; i < proto.Targets.Count; i++)
        {
            if (!TryValidateRetrievalTarget(proto.ID, i, proto.Targets[i]))
                valid = false;
        }

        if (!TryValidateRetrievalSpawn(proto))
            valid = false;

        if (!TryValidateRetrievalRewardsForPool(proto))
            valid = false;

        return valid;
    }

    private bool TryValidateRetrievalSpawn(NcRetrievalContractPrototype proto)
    {
        var spawn = proto.Spawn;
        if (spawn == null || !spawn.Enabled)
            return true;

        var valid = true;

        if (spawn.Point == null)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has spawn enabled but no spawn.point selector.");
            valid = false;
        }
        else if (!TryValidateRetrievalSpawnPointSelector(proto.ID, spawn.Point))
        {
            valid = false;
        }

        for (var i = 0; i < proto.Targets.Count; i++)
        {
            var target = proto.Targets[i];
            if (string.IsNullOrWhiteSpace(target.Group))
                continue;

            if (!_prototypes.TryIndex<NcItemGroupPrototype>(target.Group, out var group))
                continue;

            if (TryValidateRetrievalSpawnableGroup(proto.ID, i, target.Group, group))
                continue;

            valid = false;
        }

        return valid;
    }

    private bool TryValidateRetrievalSpawnPointSelector(
        string contractId,
        ContractPointSelectorPrototype selector)
    {
        return selector.Type switch
        {
            ContractPointSelectorType.MarkerId => RequireRetrievalSpawnPointId(contractId, selector),
            ContractPointSelectorType.MarkerGroup => RequireRetrievalSpawnPointId(contractId, selector),
            ContractPointSelectorType.Weighted => TryValidateRetrievalSpawnWeightedSelector(contractId, selector),
            ContractPointSelectorType.Store => RejectRetrievalStoreSpawnPoint(contractId),
            _ => RejectRetrievalUnknownSpawnPoint(contractId, selector.Type)
        };
    }

    private static bool RequireRetrievalSpawnPointId(string contractId, ContractPointSelectorPrototype selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Id))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point uses {selector.Type} but has no id.");
        return false;
    }

    private bool TryValidateRetrievalSpawnWeightedSelector(
        string contractId,
        ContractPointSelectorPrototype selector)
    {
        if (selector.Options.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' spawn.point is Weighted but has no options.");
            return false;
        }

        var valid = true;
        var usable = 0;
        for (var i = 0; i < selector.Options.Count; i++)
        {
            var option = selector.Options[i];
            if (option.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Retrieval contract '{contractId}' spawn.point option #{i} has non-positive weight={option.Weight}.");
                valid = false;
                continue;
            }

            switch (option.Type)
            {
                case ContractPointSelectorType.MarkerId:
                case ContractPointSelectorType.MarkerGroup:
                    if (string.IsNullOrWhiteSpace(option.Id))
                    {
                        Sawmill.Warning(
                            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point option #{i} uses {option.Type} but has no id.");
                        valid = false;
                        continue;
                    }

                    usable++;
                    break;

                default:
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval contract '{contractId}' spawn.point option #{i} uses unsupported type {option.Type}. " +
                        "Retrieval spawn points must use MarkerId or MarkerGroup.");
                    valid = false;
                    break;
            }
        }

        return valid && usable > 0;
    }

    private static bool RejectRetrievalStoreSpawnPoint(string contractId)
    {
        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point cannot be Store. " +
            "Use MarkerId, MarkerGroup, or Weighted marker options.");
        return false;
    }

    private static bool RejectRetrievalUnknownSpawnPoint(string contractId, ContractPointSelectorType type)
    {
        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point has unsupported selector type {type}.");
        return false;
    }

    private bool TryValidateRetrievalSpawnableGroup(
        string contractId,
        int index,
        string groupId,
        NcItemGroupPrototype group)
    {
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var prototypeId = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(prototypeId))
                continue;

            if (_prototypes.HasIndex<EntityPrototype>(prototypeId))
                return true;
        }

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' has spawn enabled but target #{index} uses group '{groupId}' " +
            "with no spawnable entity prototypes. Tags-only groups can match turn-in items, but cannot spawn retrieval items.");
        return false;
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

    private bool TryValidateRetrievalTargetCount(NcRetrievalContractPrototype proto)
    {
        if (!IsSupplyTargetCountConfigured(proto.TargetCount))
            return true;

        var range = proto.TargetCount;
        if (range.Min < 1 || range.Max < 1 || range.Min > range.Max)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has invalid targetCount range " +
                $"{range.Min}..{range.Max}. Expected min >= 1, max >= min.");
            return false;
        }

        if (proto.Targets.Count > 0 && range.Max > proto.Targets.Count)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has targetCount max={range.Max}, " +
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

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' target #{index} does not define 'count'.");
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

    private bool TryValidateRetrievalTarget(
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
                    ? $"[ContractsV2] Retrieval contract '{contractId}' target #{index} has both prototype and group. Use exactly one."
                    : $"[ContractsV2] Retrieval contract '{contractId}' target #{index} has neither prototype nor group.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' target #{index} does not define 'count'.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' target #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' target #{index} has non-positive weight={entry.Weight}. " +
                "Weight is used when targetCount is configured and must be > 0.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                return true;

            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' target #{index} references missing entity prototype " +
                $"'{entry.Prototype}'.");
            return false;
        }

        if (!_prototypes.TryIndex<NcItemGroupPrototype>(entry.Group, out var group))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' target #{index} references missing ncItemGroup " +
                $"'{entry.Group}'. Retrieval V2 group targets must reference ncItemGroup prototypes, not legacy matchers.");
            return false;
        }

        if (!TryValidateItemGroup(contractId, entry.Group, group))
            return false;

        if (TryGetContractMatcherSpec(entry.Group, out _))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' target #{index} references invalid item group '{entry.Group}'.");
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

    private bool TryValidateRetrievalRewardsForPool(NcRetrievalContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has no reward entries. " +
                "Use 'reward' as a list with type: Currency, Item or Pool. Contract skipped.");
            return false;
        }

        var valid = true;
        var hasAtLeastOneValidReward = false;

        for (var i = 0; i < proto.Reward.Count; i++)
        {
            if (TryValidateRetrievalRewardEntry(proto.ID, $"reward[{i}]", proto.Reward[i]))
                hasAtLeastOneValidReward = true;
            else
                valid = false;
        }

        if (hasAtLeastOneValidReward)
            return valid;

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }

    private bool TryValidateSupplyRewardEntry(
        string contractId,
        string path,
        NcSupplyRewardEntry entry)
    {
        if (HasLegacySupplyRewardFields(entry, out var legacyField))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' {path} uses legacy field '{legacyField}'. " +
                "Supply V2 rewards must use only type + prototype/currency/pool + count.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

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

                if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(entry.Pool, out var pool))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Supply contract '{contractId}' {path} references missing Supply V2 reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                return TryValidateRewardPoolPrototype(contractId, entry.Pool, pool);

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool TryValidateRetrievalRewardEntry(
        string contractId,
        string path,
        NcSupplyRewardEntry entry)
    {
        if (HasLegacySupplyRewardFields(entry, out var legacyField))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' {path} uses legacy field '{legacyField}'. " +
                "Retrieval V2 rewards must use only type + prototype/currency/pool + count.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min >= 0, max > 0, min <= max.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (!RequireOnlyRetrievalRewardTarget(contractId, path, nameof(entry.Prototype), entry.Prototype, entry.Currency, entry.Pool))
                    return false;

                if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Retrieval contract '{contractId}' {path} references missing entity prototype " +
                    $"'{entry.Prototype}'.");
                return false;

            case StoreRewardType.Currency:
                if (!RequireOnlyRetrievalRewardTarget(contractId, path, nameof(entry.Currency), entry.Currency, entry.Prototype, entry.Pool))
                    return false;

                if (_prototypes.HasIndex<StackPrototype>(entry.Currency))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Retrieval contract '{contractId}' {path} references missing stack currency " +
                    $"'{entry.Currency}'.");
                return false;

            case StoreRewardType.Pool:
                if (!RequireOnlyRetrievalRewardTarget(contractId, path, nameof(entry.Pool), entry.Pool, entry.Prototype, entry.Currency))
                    return false;

                if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(entry.Pool, out var pool))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval contract '{contractId}' {path} references missing Supply V2 reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                return TryValidateRewardPoolPrototype(contractId, entry.Pool, pool);

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
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

    private bool RequireOnlyRetrievalRewardTarget(
        string contractId,
        string path,
        string expectedField,
        string expectedValue,
        string otherA,
        string otherB)
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} requires field '{expectedField}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(otherA) && string.IsNullOrWhiteSpace(otherB))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' {path} has extra reward target fields. " +
            $"For each reward entry use only the field required by its type.");
        return false;
    }

    private bool TryValidateRewardPoolPrototype(
        string ownerId,
        string poolId,
        NcSupplyRewardPoolPrototype pool)
    {
        if (pool.Entries.Count == 0)
        {
            Sawmill.Warning($"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' has no entries.");
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

        Sawmill.Warning($"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' has no valid entries.");
        return false;
    }

    private bool TryValidateRewardPoolEntry(
        string ownerId,
        string poolId,
        int index,
        NcSupplyRewardPoolEntry entry)
    {
        if (HasLegacySupplyRewardPoolFields(entry, out var legacyField))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} uses legacy field '{legacyField}'. " +
                "Supply V2 reward pools must use only type + prototype/currency + count + weight/max.");
            return false;
        }

        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has non-positive weight={entry.Weight}.");
            return false;
        }

        if (entry.MaxRepeats < 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has negative max={entry.MaxRepeats}.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min >= 0, max > 0, min <= max.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (!RequireOnlyPoolRewardTarget(ownerId, poolId, index, "prototype", entry.Prototype, entry.Currency))
                    return false;

                if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} references missing entity prototype " +
                    $"'{entry.Prototype}'.");
                return false;

            case StoreRewardType.Currency:
                if (!RequireOnlyPoolRewardTarget(ownerId, poolId, index, "currency", entry.Currency, entry.Prototype))
                    return false;

                if (_prototypes.HasIndex<StackPrototype>(entry.Currency))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} references missing stack currency " +
                    $"'{entry.Currency}'.");
                return false;

            case StoreRewardType.Pool:
                Sawmill.Warning(
                    $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} is a nested Pool. " +
                    "Nested pools are not supported for Supply V2 rewards.");
                return false;

            case StoreRewardType.Unspecified:
                Sawmill.Warning(
                    $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning(
                    $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool RequireOnlyPoolRewardTarget(
        string ownerId,
        string poolId,
        int index,
        string expectedField,
        string expectedValue,
        string other)
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} requires field '{expectedField}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(other))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has extra reward target fields. " +
            "Use only prototype for Item or currency for Currency.");
        return false;
    }

    private static bool HasLegacySupplyRewardFields(NcSupplyRewardEntry entry, out string field)
    {
        if (IsLegacyTrapRangeConfigured(entry.LegacyAmount))
        {
            field = "amount";
            return true;
        }

        if (!float.IsNaN(entry.LegacyProbability))
        {
            field = "prob";
            return true;
        }

        if (!float.IsNaN(entry.LegacyChance))
        {
            field = "chance";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.LegacyId))
        {
            field = "id";
            return true;
        }

        if (entry.LegacyOptions is { Count: > 0 })
        {
            field = "options";
            return true;
        }

        field = string.Empty;
        return false;
    }

    private static bool HasLegacySupplyRewardPoolFields(NcSupplyRewardPoolEntry entry, out string field)
    {
        if (IsLegacyTrapRangeConfigured(entry.LegacyAmount))
        {
            field = "amount";
            return true;
        }

        if (!float.IsNaN(entry.LegacyProbability))
        {
            field = "prob";
            return true;
        }

        if (!float.IsNaN(entry.LegacyChance))
        {
            field = "chance";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.LegacyId))
        {
            field = "id";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.LegacyPool))
        {
            field = "pool";
            return true;
        }

        if (entry.LegacyOptions is { Count: > 0 })
        {
            field = "options";
            return true;
        }

        field = string.Empty;
        return false;
    }

    private static bool IsLegacyTrapRangeConfigured(IntRange range)
    {
        return range.Min != int.MinValue || range.Max != int.MinValue;
    }

    private static bool IsRewardCountRange(IntRange range)
    {
        return range.Min >= 0 && range.Max > 0 && range.Min <= range.Max;
    }

    private static bool IsCountConfigured(IntRange range)
    {
        return range.Min > 0 || range.Max > 0;
    }


}
