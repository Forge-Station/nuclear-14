using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateGhostRoleContractForPool(string poolId, NcGhostRoleContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[ContractsV2] Offer pool '{poolId}' contains a ghost role contract with an empty prototype id.");
            return false;
        }

        if (!_prototypes.TryIndex<NcGhostRolePresetPrototype>(proto.Role.Id, out var role))
        {
            Sawmill.Warning(
                $"[ContractsV2] GhostRole contract '{proto.ID}' references missing ncGhostRolePreset '{proto.Role}'.");
            valid = false;
        }
        else if (!TryValidateGhostRolePreset(proto.ID, role))
        {
            valid = false;
        }

        if (!TryValidateGhostRoleSpawn(proto.ID, proto.Spawn))
            valid = false;

        if (!TryValidateGhostRoleCompletion(proto.ID, proto.Completion))
            valid = false;

        if (!TryValidateGhostRoleRewardsForPool(proto))
            valid = false;

        return valid;
    }

    private bool TryValidateGhostRolePreset(string contractId, NcGhostRolePresetPrototype role)
    {
        if (string.IsNullOrWhiteSpace(role.EntityPrototype))
        {
            Sawmill.Warning($"[ContractsV2] GhostRole contract '{contractId}' role preset '{role.ID}' has no entityPrototype.");
            return false;
        }

        if (_prototypes.HasIndex<EntityPrototype>(role.EntityPrototype))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] GhostRole contract '{contractId}' role preset '{role.ID}' references missing entity prototype '{role.EntityPrototype}'.");
        return false;
    }

    private bool TryValidateGhostRoleSpawn(string contractId, NcGhostRoleSpawnData spawn)
    {
        if (spawn.Point == null)
        {
            Sawmill.Warning($"[ContractsV2] GhostRole contract '{contractId}' must define spawn.point.");
            return false;
        }

        if (spawn.AcceptTimeoutSeconds < 0)
        {
            Sawmill.Warning($"[ContractsV2] GhostRole contract '{contractId}' spawn.acceptTimeoutSeconds must be >= 0.");
            return false;
        }

        return spawn.Point.Type switch
        {
            ContractPointSelectorType.MarkerId => RequireGhostRoleSpawnPointId(contractId, spawn.Point),
            ContractPointSelectorType.MarkerGroup => RequireGhostRoleSpawnPointId(contractId, spawn.Point),
            ContractPointSelectorType.Weighted => TryValidateGhostRoleSpawnWeightedSelector(contractId, spawn.Point),
            ContractPointSelectorType.Store => RejectGhostRoleStoreSpawnPoint(contractId),
            _ => RejectGhostRoleUnknownSpawnPoint(contractId, spawn.Point.Type)
        };
    }

    private static bool RequireGhostRoleSpawnPointId(string contractId, ContractPointSelectorPrototype selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Id))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] GhostRole contract '{contractId}' spawn.point type {selector.Type} requires id.");
        return false;
    }

    private bool TryValidateGhostRoleSpawnWeightedSelector(string contractId, ContractPointSelectorPrototype selector)
    {
        if (selector.Options.Count == 0)
        {
            Sawmill.Warning($"[ContractsV2] GhostRole contract '{contractId}' spawn.point weighted selector has no options.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < selector.Options.Count; i++)
        {
            var option = selector.Options[i];
            if (option.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] GhostRole contract '{contractId}' spawn.point options[{i}] weight must be > 0.");
                valid = false;
            }

            switch (option.Type)
            {
                case ContractPointSelectorType.MarkerId:
                case ContractPointSelectorType.MarkerGroup:
                    if (string.IsNullOrWhiteSpace(option.Id))
                    {
                        Sawmill.Warning(
                            $"[ContractsV2] GhostRole contract '{contractId}' spawn.point options[{i}] type {option.Type} requires id.");
                        valid = false;
                    }
                    break;

                default:
                    Sawmill.Warning(
                        $"[ContractsV2] GhostRole contract '{contractId}' spawn.point options[{i}] type {option.Type} is not supported. Use MarkerId or MarkerGroup.");
                    valid = false;
                    break;
            }
        }

        return valid;
    }

    private static bool RejectGhostRoleStoreSpawnPoint(string contractId)
    {
        Sawmill.Warning(
            $"[ContractsV2] GhostRole contract '{contractId}' spawn.point.type=Store is forbidden. Ghost role spawners must use contract markers.");
        return false;
    }

    private static bool RejectGhostRoleUnknownSpawnPoint(string contractId, ContractPointSelectorType type)
    {
        Sawmill.Warning(
            $"[ContractsV2] GhostRole contract '{contractId}' spawn.point.type={type} is not supported.");
        return false;
    }

    private static bool TryValidateGhostRoleCompletion(string contractId, NcGhostRoleCompletionData completion)
    {
        return completion.Mode is
            NcGhostRoleCompletionMode.DeadBodyTurnIn or
            NcGhostRoleCompletionMode.AliveCuffedTurnIn;
    }

    private bool TryValidateGhostRoleRewardsForPool(NcGhostRoleContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] GhostRole contract '{proto.ID}' has no reward entries. " +
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
            $"[ContractsV2] GhostRole contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }
}
