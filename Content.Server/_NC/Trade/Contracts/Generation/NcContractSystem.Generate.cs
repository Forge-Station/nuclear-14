using System;
using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateContractData(EntityUid store, StoreContractPrototype proto)
    {
        var targets = BuildContractTargets(store, proto);
        var totalRequired = CalculateTotalRequired(targets);
        var mainTarget = GetPrimaryTargetId(targets);

        var objectiveType = proto.ObjectiveType;
        var runtime = CreateInitialRuntimeState(proto);
        var config = CreateObjectiveConfig(proto);
        ApplyStageObjectiveContractShape(objectiveType, runtime, config, targets, ref totalRequired, ref mainTarget);

        var contract = new ContractServerData
        {
            Id = proto.ID,
            Name = proto.Name,
            Difficulty = proto.Difficulty,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = objectiveType,
            Runtime = runtime,
            Config = config,
            FlowStatus = ContractFlowStatus.Available,
            Targets = targets,
            TargetItem = mainTarget,
            Required = totalRequired,
            Progress = 0,
            Rewards = BakeRewardsForContract(store, proto)
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private List<ContractTargetServerData> BuildContractTargets(EntityUid store, StoreContractPrototype proto)
    {
        var baseTargetItem = proto.TargetItem ?? string.Empty;
        var baseRequired = RollFair(new(QuasiKeyKind.Req, store, proto.ID, null), proto.Required, 1);
        var fallbackTarget = CreateFallbackContractTarget(baseTargetItem, baseRequired, proto.MatchMode);

        var targets = proto.Targets is { Count: > 0 }
            ? BuildWeightedContractTargets(store, proto, baseRequired)
            : new List<ContractTargetServerData>();

        if (targets.Count == 0 && fallbackTarget != null)
            targets.Add(fallbackTarget);

        return targets;
    }

    private List<ContractTargetServerData> BuildWeightedContractTargets(
        EntityUid store,
        StoreContractPrototype proto,
        int baseRequired
    )
    {
        var targetCount = Math.Max(1, RollFair(new(QuasiKeyKind.Tc, store, proto.ID, null), proto.TargetCount, 1));
        var pool = new List<StoreContractTargetEntry>(proto.Targets!);
        var picks = Math.Min(targetCount, pool.Count);
        var targets = new List<ContractTargetServerData>(picks);

        for (var i = 0; i < picks && pool.Count > 0; i++)
        {
            var chosen = PickWeighted(_random, pool, static t => t.Weight);
            pool.Remove(chosen);

            var required = RollTargetRequirement(store, proto.ID, chosen, baseRequired);
            var target = CreateFallbackContractTarget(chosen.TargetItemId, required, proto.MatchMode);
            if (target != null)
                targets.Add(target);
        }

        return targets;
    }

    private int RollTargetRequirement(
        EntityUid store,
        string contractProtoId,
        StoreContractTargetEntry chosen,
        int baseRequired
    )
    {
        var rolledReq = RollFair(
            new(QuasiKeyKind.TReq, store, contractProtoId, chosen.TargetItemId),
            chosen.Required,
            1);

        return rolledReq > 0 ? rolledReq : baseRequired;
    }

    private static ContractTargetServerData? CreateFallbackContractTarget(
        string targetItem,
        int required,
        PrototypeMatchMode matchMode
    )
    {
        if (string.IsNullOrWhiteSpace(targetItem) || required <= 0)
            return null;

        return new()
        {
            TargetItem = targetItem,
            Required = required,
            Progress = 0,
            MatchMode = matchMode
        };
    }

    private static int CalculateTotalRequired(List<ContractTargetServerData> targets)
    {
        var totalRequired = 0;
        foreach (var target in targets)
            totalRequired = SaturatingAdd(totalRequired, Math.Max(0, target.Required));

        return totalRequired;
    }

    private static string GetPrimaryTargetId(List<ContractTargetServerData> targets)
    {
        return targets.Count > 0 ? targets[0].TargetItem : string.Empty;
    }

    private static void ApplyStageObjectiveContractShape(
        ContractObjectiveType objectiveType,
        ContractRuntimeContextData runtime,
        ContractObjectiveConfigData config,
        List<ContractTargetServerData> targets,
        ref int totalRequired,
        ref string mainTarget
    )
    {
        var executionKind = ContractExecutionKinds.Resolve(objectiveType, config.TargetPrototype);
        if (!ContractExecutionKinds.UsesStageProgress(executionKind))
            return;

        targets.Clear();
        totalRequired = Math.Max(1, runtime.StageGoal);
        mainTarget = ResolveObjectiveTargetId(config);
    }
}
