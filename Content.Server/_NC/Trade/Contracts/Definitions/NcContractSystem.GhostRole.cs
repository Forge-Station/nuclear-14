using System.Linq;
using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateGhostRoleContractData(EntityUid store, NcGhostRoleContractPrototype proto)
    {
        var role = _prototypes.Index<NcGhostRolePresetPrototype>(proto.Role.Id);
        var rewards = BakeRewardsForContract(store, proto.ID, BuildGhostRoleRewardDefs(proto));

        var runtime = new ContractRuntimeContextData
        {
            Stage = 0,
            StageGoal = 1,
            AcceptTimeoutRemainingSeconds = 0,
            GhostRolePendingAcceptance = false,
            Failed = false,
            FailureReason = string.Empty
        };
        NormalizeRuntimeState(ContractExecutionKind.GhostRoleObjective, runtime);

        var config = new ContractObjectiveConfigData
        {
            GhostRole = proto.Role.Id,
            GhostRolePrototype = role.EntityPrototype,
            GhostRoleName = role.Name,
            GhostRoleDescription = role.Description,
            GhostRoleRules = role.Rules,
            GhostRoleRequirements = new(role.Requirements),
            GhostRoleCharacterName = role.Character.Name,
            GhostRoleCharacterSex = role.Character.Sex,
            GhostRoleCharacterGender = role.Character.Gender,
            GhostRoleCharacterAge = role.Character.Age,
            GhostRoleCharacterHair = role.Character.Hair,
            GhostRoleCharacterHairColor = role.Character.HairColor,
            GhostRolePerks = role.Perks.Select(p => p.Id).ToList(),
            GhostRoleCompletionMode = proto.Completion.Mode,
            GhostRoleSurvivalDurationSeconds = proto.Survival.DurationSeconds,
            GhostRoleSurvivalBriefing = proto.Survival.Briefing,
            GhostRoleSurvivalObjectiveTitle = proto.Survival.ObjectiveTitle,
            GhostRoleSurvivalObjectiveDescription = proto.Survival.ObjectiveDescription,
            AcceptTimeoutSeconds = proto.Spawn.AcceptTimeoutSeconds,
            SpawnPoint = CloneContractPointSelector(proto.Spawn.Point),
            GivePinpointer = true
        };
        NormalizeObjectiveConfig(config);

        var target = string.IsNullOrWhiteSpace(role.EntityPrototype)
            ? proto.ID
            : role.EntityPrototype;

        var contract = new ContractServerData
        {
            Id = proto.ID,
            Name = proto.Name,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = ContractObjectiveType.GhostRole,
            ExecutionKind = ContractExecutionKind.GhostRoleObjective,
            Runtime = runtime,
            Config = config,
            FlowStatus = ContractFlowStatus.Available,
            MatchMode = PrototypeMatchMode.Exact,
            TargetItem = target,
            Required = 1,
            Progress = 0,
            Targets = new()
            {
                new ContractTargetServerData
                {
                    TargetItem = target,
                    Required = 1,
                    Progress = 0,
                    MatchMode = PrototypeMatchMode.Exact
                }
            },
            Rewards = rewards
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private List<ContractRewardDef> BuildGhostRoleRewardDefs(NcGhostRoleContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
            TryAppendGhostRoleRewardEntry(proto.ID, $"reward[{i}]", proto.Reward[i], rewards);

        return rewards;
    }

    private bool TryAppendGhostRoleRewardEntry(
        string contractId,
        string path,
        NcSupplyRewardEntry entry,
        List<ContractRewardDef> output)
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] GhostRole contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] GhostRole contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[Contracts] GhostRole contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                {
                    Sawmill.Warning(
                        $"[Contracts] GhostRole contract '{contractId}' {path} references missing entity prototype '{entry.Prototype}'.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Item,
                    RewardId = entry.Prototype,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Currency:
                if (string.IsNullOrWhiteSpace(entry.Currency))
                {
                    Sawmill.Warning($"[Contracts] GhostRole contract '{contractId}' {path} is Currency but has no currency.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Currency,
                    RewardId = entry.Currency,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Pool:
                if (string.IsNullOrWhiteSpace(entry.Pool))
                {
                    Sawmill.Warning($"[Contracts] GhostRole contract '{contractId}' {path} is Pool but has no pool id.");
                    return false;
                }

                if (!_prototypes.HasIndex<NcSupplyRewardPoolPrototype>(entry.Pool))
                {
                    Sawmill.Warning(
                        $"[Contracts] GhostRole contract '{contractId}' {path} references missing Supply reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Pool,
                    RewardId = entry.Pool,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[Contracts] GhostRole contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning($"[Contracts] GhostRole contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }
}
