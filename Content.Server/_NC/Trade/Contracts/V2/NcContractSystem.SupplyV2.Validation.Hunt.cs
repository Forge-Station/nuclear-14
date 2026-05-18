using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateHuntContractForPool(string packId, NcHuntContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[ContractsV2] Pack '{packId}' contains a hunt contract with an empty prototype id.");
            return false;
        }

        if (!TryValidateHuntLegacyFields(proto))
            valid = false;

        if (!TryValidateHuntTargets(proto.ID, proto.Targets))
            valid = false;

        if (!TryValidateHuntCompletion(proto.ID, proto.Completion))
            valid = false;

        if (!TryValidateHuntSpawn(proto.ID, proto.Spawn))
            valid = false;

        if (!TryValidateHuntRewardsForPool(proto))
            valid = false;

        return valid;
    }

    private bool TryValidateHuntSpawn(string contractId, NcHuntSpawnData spawn)
    {
        if (spawn.Point == null)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' must define spawn.point.");
            return false;
        }

        return spawn.Point.Type switch
        {
            ContractPointSelectorType.MarkerId => RequireHuntSpawnPointId(contractId, spawn.Point),
            ContractPointSelectorType.MarkerGroup => RequireHuntSpawnPointId(contractId, spawn.Point),
            ContractPointSelectorType.Weighted => TryValidateHuntSpawnWeightedSelector(contractId, spawn.Point),
            ContractPointSelectorType.Store => RejectHuntStoreSpawnPoint(contractId),
            _ => RejectHuntUnknownSpawnPoint(contractId, spawn.Point.Type)
        };
    }

    private static bool RequireHuntSpawnPointId(string contractId, ContractPointSelectorPrototype selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Id))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Hunt contract '{contractId}' spawn.point type {selector.Type} requires id.");
        return false;
    }

    private bool TryValidateHuntSpawnWeightedSelector(string contractId, ContractPointSelectorPrototype selector)
    {
        if (selector.Options.Count == 0)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' spawn.point weighted selector has no options.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < selector.Options.Count; i++)
        {
            var option = selector.Options[i];
            if (option.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Hunt contract '{contractId}' spawn.point options[{i}] weight must be > 0.");
                valid = false;
            }

            switch (option.Type)
            {
                case ContractPointSelectorType.MarkerId:
                case ContractPointSelectorType.MarkerGroup:
                    if (string.IsNullOrWhiteSpace(option.Id))
                    {
                        Sawmill.Warning(
                            $"[ContractsV2] Hunt contract '{contractId}' spawn.point options[{i}] type {option.Type} requires id.");
                        valid = false;
                    }
                    break;

                default:
                    Sawmill.Warning(
                        $"[ContractsV2] Hunt contract '{contractId}' spawn.point options[{i}] type {option.Type} is not supported. Use MarkerId or MarkerGroup.");
                    valid = false;
                    break;
            }
        }

        return valid;
    }

    private static bool RejectHuntStoreSpawnPoint(string contractId)
    {
        Sawmill.Warning(
            $"[ContractsV2] Hunt contract '{contractId}' spawn.point.type=Store is forbidden. Hunt V2 targets must spawn at contract markers.");
        return false;
    }

    private static bool RejectHuntUnknownSpawnPoint(string contractId, ContractPointSelectorType type)
    {
        Sawmill.Warning(
            $"[ContractsV2] Hunt contract '{contractId}' spawn.point.type={type} is not supported.");
        return false;
    }

    private bool TryValidateHuntTargets(string contractId, List<NcHuntTargetData> targets)
    {
        if (targets.Count == 0)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' must define at least one targets entry.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < targets.Count; i++)
        {
            if (!TryValidateHuntTarget(contractId, i, targets[i]))
                valid = false;
        }

        return valid;
    }

    private bool TryValidateHuntTarget(string contractId, int index, NcHuntTargetData target)
    {
        var hasGroup = !string.IsNullOrWhiteSpace(target.Group);
        var hasPrototype = !string.IsNullOrWhiteSpace(target.Prototype);

        if (hasGroup == hasPrototype)
        {
            Sawmill.Warning(
                hasGroup
                    ? $"[ContractsV2] Hunt contract '{contractId}' targets[{index}] has both group and prototype. Use exactly one."
                    : $"[ContractsV2] Hunt contract '{contractId}' targets[{index}] has neither group nor prototype.");
            return false;
        }

        if (target.Count <= 0)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' targets[{index}] count must be > 0.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(target.Prototype))
                return true;

            Sawmill.Warning(
                $"[ContractsV2] Hunt contract '{contractId}' targets[{index}] references missing entity prototype '{target.Prototype}'.");
            return false;
        }

        if (!_prototypes.TryIndex<NcHuntGroupPrototype>(target.Group, out var group))
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt contract '{contractId}' targets[{index}] references missing ncHuntGroup '{target.Group}'.");
            return false;
        }

        return TryValidateHuntGroup(contractId, target.Group, group);
    }

    private bool TryValidateHuntGroup(string ownerId, string groupId, NcHuntGroupPrototype group)
    {
        if (group.Prototypes.Count == 0)
        {
            Sawmill.Warning($"[ContractsV2] Hunt group '{groupId}' used by '{ownerId}' has no prototypes.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var proto = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(proto))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Hunt group '{groupId}' used by '{ownerId}' has empty prototypes[{i}].");
                valid = false;
                continue;
            }

            if (_prototypes.HasIndex<EntityPrototype>(proto))
                continue;

            Sawmill.Warning(
                $"[ContractsV2] Hunt group '{groupId}' used by '{ownerId}' references missing entity prototype '{proto}'.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateHuntCompletion(string contractId, NcHuntCompletionData completion)
    {
        switch (completion.Mode)
        {
            case NcHuntCompletionMode.ConfirmedKill:
                if (string.IsNullOrWhiteSpace(completion.Trophy))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Hunt contract '{contractId}' completion.mode=ConfirmedKill must not define trophy.");
                return false;

            case NcHuntCompletionMode.TrophyTurnIn:
                if (string.IsNullOrWhiteSpace(completion.Trophy))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Hunt contract '{contractId}' completion.mode=TrophyTurnIn requires trophy.");
                    return false;
                }

                if (_prototypes.HasIndex<EntityPrototype>(completion.Trophy))
                    return true;

                Sawmill.Warning(
                    $"[ContractsV2] Hunt contract '{contractId}' references missing trophy prototype '{completion.Trophy}'.");
                return false;

            default:
                Sawmill.Warning(
                    $"[ContractsV2] Hunt contract '{contractId}' completion.mode={completion.Mode} is not supported.");
                return false;
        }
    }

    private bool TryValidateHuntRewardsForPool(NcHuntContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt contract '{proto.ID}' has no reward entries. " +
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
            $"[ContractsV2] Hunt contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }

    private bool TryValidateHuntLegacyFields(NcHuntContractPrototype proto)
    {
        if (!string.IsNullOrWhiteSpace(proto.LegacyTargetItem))
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{proto.ID}' uses legacy field 'targetItem'.");
            return false;
        }

        if (IsLegacyTrapRangeConfigured(proto.LegacyRequired))
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{proto.ID}' uses legacy field 'required'.");
            return false;
        }

        if (proto.LegacyMatchMode is not null)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{proto.ID}' uses legacy field 'match'.");
            return false;
        }

        if (proto.LegacyObjectiveType is not null)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{proto.ID}' uses legacy field 'objectiveType'.");
            return false;
        }

        if (proto.LegacyRuntime is not null)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{proto.ID}' uses legacy field 'runtime'.");
            return false;
        }

        if (proto.LegacyTarget is not null)
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{proto.ID}' uses legacy field 'target'. Use targets as a list.");
            return false;
        }

        if (IsLegacyTrapRangeConfigured(proto.LegacyTargetCount))
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{proto.ID}' uses legacy field 'targetCount'.");
            return false;
        }

        return true;
    }
}
