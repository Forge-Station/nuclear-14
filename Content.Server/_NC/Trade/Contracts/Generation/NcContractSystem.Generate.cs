using System;
using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateContractData(EntityUid store, StoreContractPrototype proto)
    {
        var targets = new List<ContractTargetServerData>();

        var baseTargetItem = proto.TargetItem ?? string.Empty;
        var baseRequired = RollFair(new(QuasiKeyKind.Req, store, proto.ID, null), proto.Required, 1);

        if (proto.Targets is { Count: > 0, })
        {
            var targetCount = RollFair(new(QuasiKeyKind.Tc, store, proto.ID, null), proto.TargetCount, 1);
            if (targetCount <= 0)
                targetCount = 1;

            var pool = new List<StoreContractTargetEntry>(proto.Targets);
            var picks = Math.Min(targetCount, pool.Count);

            for (var i = 0; i < picks && pool.Count > 0; i++)
            {
                var chosen = PickWeighted(_random, pool, t => t.Weight);
                pool.Remove(chosen);

                var itemId = chosen.TargetItemId;
                var rolledReq = RollFair(
                    new(QuasiKeyKind.TReq, store, proto.ID, chosen.TargetItemId),
                    chosen.Required,
                    1);

                var req = rolledReq > 0 ? rolledReq : baseRequired;
                targets.Add(
                    new()
                    {
                        TargetItem = itemId,
                        Required = req,
                        Progress = 0,
                        MatchMode = proto.MatchMode
                    });
            }

            if (targets.Count == 0 && !string.IsNullOrWhiteSpace(baseTargetItem) && baseRequired > 0)
            {
                targets.Add(
                    new()
                    {
                        TargetItem = baseTargetItem,
                        Required = baseRequired,
                        Progress = 0,
                        MatchMode = proto.MatchMode
                    });
            }
        }
        else if (!string.IsNullOrWhiteSpace(baseTargetItem) && baseRequired > 0)
        {
            targets.Add(
                new()
                {
                    TargetItem = baseTargetItem,
                    Required = baseRequired,
                    Progress = 0,
                    MatchMode = proto.MatchMode
                });
        }

        var totalRequired = 0;
        foreach (var t in targets)
            totalRequired += Math.Max(0, t.Required);

        var mainTarget = targets.Count > 0 ? targets[0].TargetItem : string.Empty;

        var objectiveType = proto.ObjectiveType;
        var runtime = BuildInitialRuntimeContext(proto);

        if (objectiveType != ContractObjectiveType.Delivery)
        {
            targets.Clear();
            totalRequired = Math.Max(1, runtime.StageGoal);

            if (!string.IsNullOrWhiteSpace(runtime.TargetPrototype))
                mainTarget = runtime.TargetPrototype;
            else if (!string.IsNullOrWhiteSpace(runtime.StructurePrototype))
                mainTarget = runtime.StructurePrototype;
            else if (!string.IsNullOrWhiteSpace(runtime.GhostRolePrototype))
                mainTarget = runtime.GhostRolePrototype;
        }

        var rewards = BakeRewardsForContract(store, proto);

        return new()
        {
            Id = proto.ID,
            Name = proto.Name,
            Difficulty = proto.Difficulty,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = objectiveType,
            Runtime = runtime,

            Targets = targets,
            TargetItem = mainTarget,
            Required = totalRequired,
            Progress = 0,

            Rewards = rewards
        };
    }

    private static ContractRuntimeContextData BuildInitialRuntimeContext(StoreContractPrototype proto)
    {
        var runtimeProto = proto.Runtime;

        var stageGoal = runtimeProto.StageGoal;
        if (stageGoal <= 0)
            stageGoal = proto.ObjectiveType == ContractObjectiveType.Repair ? 3 : 1;

        var pinpointerPrototype = runtimeProto.PinpointerPrototype;
        if (string.IsNullOrWhiteSpace(pinpointerPrototype))
            pinpointerPrototype = "PinpointerUniversal";

        return new ContractRuntimeContextData
        {
            Stage = 0,
            StageGoal = stageGoal,
            AcceptTimeoutSeconds = Math.Max(0, runtimeProto.AcceptTimeoutSeconds),
            Failed = false,
            FailureReason = string.Empty,
            SpawnPointTag = runtimeProto.SpawnPointTag ?? string.Empty,
            TargetPrototype = runtimeProto.TargetPrototype ?? string.Empty,
            StructurePrototype = runtimeProto.StructurePrototype ?? string.Empty,
            GhostRolePrototype = runtimeProto.GhostRolePrototype ?? string.Empty,
            GivePinpointer = runtimeProto.GivePinpointer,
            PinpointerPrototype = pinpointerPrototype,
            GuardPrototype = runtimeProto.GuardPrototype ?? string.Empty,
            GuardCount = Math.Max(0, runtimeProto.GuardCount)
        };
    }
}
