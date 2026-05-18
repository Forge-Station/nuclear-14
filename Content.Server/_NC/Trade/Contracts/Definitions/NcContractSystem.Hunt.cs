using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateHuntContractData(EntityUid store, NcHuntContractPrototype proto)
    {
        var targets = BuildHuntTargets(proto);
        var required = Math.Max(1, CalculateTotalRequired(targets));
        var mainTarget = GetPrimaryTargetId(targets);
        var bodyTarget = ResolveHuntBodyPrototype(targets);
        var rewards = BakeRewardsForContract(store, proto.ID, BuildHuntRewardDefs(store, proto));

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
            HuntEnabled = true,
            HuntCompletionMode = proto.Completion.Mode,
            HuntBodyPrototype = proto.Completion.Mode == NcHuntCompletionMode.BodyTurnIn
                ? bodyTarget
                : string.Empty,
            SpawnPoint = CloneContractPointSelector(proto.Spawn.Point)
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
            MatchMode = targets.Count > 0 ? targets[0].MatchMode : PrototypeMatchMode.Exact,
            Targets = targets,
            TargetItem = mainTarget,
            Required = required,
            Progress = 0,
            Rewards = rewards
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private static List<ContractTargetServerData> BuildHuntTargets(NcHuntContractPrototype proto)
    {
        var targets = new List<ContractTargetServerData>(proto.Targets.Count);
        for (var i = 0; i < proto.Targets.Count; i++)
        {
            var target = proto.Targets[i];
            var hasPrototype = !string.IsNullOrWhiteSpace(target.Prototype);
            targets.Add(new ContractTargetServerData
            {
                TargetItem = hasPrototype ? target.Prototype : target.Group,
                Required = Math.Max(1, target.Count),
                Progress = 0,
                BodyRequired = target.Body,
                MatchMode = hasPrototype ? PrototypeMatchMode.Exact : PrototypeMatchMode.Matcher
            });
        }

        return targets;
    }

    private static string ResolveHuntBodyPrototype(List<ContractTargetServerData> targets)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target.BodyRequired && target.MatchMode == PrototypeMatchMode.Exact)
                return target.TargetItem;
        }

        return string.Empty;
    }

    private List<ContractRewardDef> BuildHuntRewardDefs(EntityUid store, NcHuntContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
            TryAppendHuntRewardEntry(store, proto.ID, $"reward[{i}]", proto.Reward[i], rewards);

        return rewards;
    }

    private bool TryAppendHuntRewardEntry(
        EntityUid store,
        string contractId,
        string path,
        NcSupplyRewardEntry entry,
        List<ContractRewardDef> output)
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                {
                    Sawmill.Warning(
                        $"[Contracts] Hunt contract '{contractId}' {path} references missing entity prototype '{entry.Prototype}'.");
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
                    Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path} is Currency but has no currency.");
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
                    Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path} is Pool but has no pool id.");
                    return false;
                }

                if (!_prototypes.HasIndex<NcSupplyRewardPoolPrototype>(entry.Pool))
                {
                    Sawmill.Warning(
                        $"[Contracts] Hunt contract '{contractId}' {path} references missing Supply reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
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
                Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }
}
