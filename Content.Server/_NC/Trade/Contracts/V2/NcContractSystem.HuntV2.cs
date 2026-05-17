using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateHuntContractData(EntityUid store, NcHuntContractPrototype proto)
    {
        var target = BuildHuntV2Target(proto);
        var required = Math.Max(1, target.Required);
        var rewards = BakeRewardsForContract(store, proto.ID, BuildHuntV2RewardDefs(store, proto));

        var runtime = new ContractRuntimeContextData
        {
            Stage = 0,
            StageGoal = required,
            AcceptTimeoutRemainingSeconds = 0,
            GhostRolePendingAcceptance = false,
            Failed = false,
            FailureReason = string.Empty
        };
        NormalizeRuntimeState(ContractExecutionKind.HuntObjective, runtime);

        var config = new ContractObjectiveConfigData
        {
            GivePinpointer = true,
            ProofPrototype = proto.Completion.Mode == NcHuntCompletionMode.TrophyTurnIn
                ? proto.Completion.Trophy
                : string.Empty,
            HuntV2Enabled = true,
            HuntV2CompletionMode = proto.Completion.Mode,
            HuntV2TargetGroup = proto.Target.Group,
            HuntV2TargetPrototype = proto.Target.Prototype
        };
        NormalizeObjectiveConfig(config);

        var contract = new ContractServerData
        {
            Id = proto.ID,
            Name = proto.Name,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = ContractObjectiveType.Hunt,
            Runtime = runtime,
            Config = config,
            FlowStatus = ContractFlowStatus.Available,
            MatchMode = target.MatchMode,
            Targets = new List<ContractTargetServerData> { target },
            TargetItem = target.TargetItem,
            Required = required,
            Progress = 0,
            Rewards = rewards
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private static ContractTargetServerData BuildHuntV2Target(NcHuntContractPrototype proto)
    {
        var hasPrototype = !string.IsNullOrWhiteSpace(proto.Target.Prototype);
        return new ContractTargetServerData
        {
            TargetItem = hasPrototype ? proto.Target.Prototype : proto.Target.Group,
            Required = Math.Max(1, proto.Target.Count),
            Progress = 0,
            MatchMode = hasPrototype ? PrototypeMatchMode.Exact : PrototypeMatchMode.Matcher
        };
    }

    private List<ContractRewardDef> BuildHuntV2RewardDefs(EntityUid store, NcHuntContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
            TryAppendHuntV2RewardEntry(store, proto.ID, $"reward[{i}]", proto.Reward[i], rewards);

        return rewards;
    }

    private bool TryAppendHuntV2RewardEntry(
        EntityUid store,
        string contractId,
        string path,
        NcSupplyRewardEntry entry,
        List<ContractRewardDef> output)
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Hunt contract '{contractId}' {path} references missing entity prototype '{entry.Prototype}'.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Item,
                    Prototype = entry.Prototype,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Currency:
                if (string.IsNullOrWhiteSpace(entry.Currency))
                {
                    Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' {path} is Currency but has no currency.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Currency,
                    Currency = entry.Currency,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Pool:
                if (string.IsNullOrWhiteSpace(entry.Pool))
                {
                    Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' {path} is Pool but has no pool id.");
                    return false;
                }

                if (!_prototypes.HasIndex<NcSupplyRewardPoolPrototype>(entry.Pool))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Hunt contract '{contractId}' {path} references missing Supply V2 reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Pool,
                    Pool = entry.Pool,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning($"[ContractsV2] Hunt contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }
}
