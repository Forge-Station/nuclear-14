using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static ContractRuntimeContextData CreateInitialRuntimeState(StoreContractPrototype proto)
    {
        var executionKind = ContractExecutionKinds.Resolve(proto.ObjectiveType, proto.Runtime.TargetPrototype);
        var runtime = new ContractRuntimeContextData
        {
            Stage = 0,
            StageGoal = proto.Runtime.StageGoal,
            AcceptTimeoutRemainingSeconds = 0,
            GhostRolePendingAcceptance = false,
            Failed = false,
            FailureReason = string.Empty
        };

        NormalizeRuntimeState(executionKind, runtime);
        return runtime;
    }

    private static ContractObjectiveConfigData CreateObjectiveConfig(StoreContractPrototype proto)
    {
        var runtimeProto = proto.Runtime;
        var config = new ContractObjectiveConfigData
        {
            AcceptTimeoutSeconds = runtimeProto.AcceptTimeoutSeconds,
            SpawnPointTag = runtimeProto.SpawnPointTag ?? string.Empty,
            TargetPrototype = runtimeProto.TargetPrototype ?? string.Empty,
            DeliverySpawnPrototype = runtimeProto.DeliverySpawnPrototype ?? string.Empty,
            StructurePrototype = runtimeProto.StructurePrototype ?? string.Empty,
            GhostRolePrototype = runtimeProto.GhostRolePrototype ?? string.Empty,
            GivePinpointer = runtimeProto.GivePinpointer,
            PinpointerPrototype = runtimeProto.PinpointerPrototype ?? string.Empty,
            GuardPrototype = runtimeProto.GuardPrototype ?? string.Empty,
            GuardCount = runtimeProto.GuardCount,
            RepairToolQuality = runtimeProto.RepairToolQuality ?? string.Empty,
            RepairDoAfterSeconds = runtimeProto.RepairDoAfterSeconds,
            RepairStageSound = runtimeProto.RepairStageSound ?? string.Empty
        };

        NormalizeObjectiveConfig(config);
        return config;
    }

    private static void NormalizeRuntimeState(ContractExecutionKind executionKind, ContractRuntimeContextData runtime)
    {
        runtime.StageGoal = runtime.StageGoal > 0
            ? runtime.StageGoal
            : GetDefaultObjectiveStageGoal(executionKind);
        runtime.Stage = Math.Clamp(runtime.Stage, 0, runtime.StageGoal);
        runtime.AcceptTimeoutRemainingSeconds = Math.Max(0, runtime.AcceptTimeoutRemainingSeconds);
        runtime.FailureReason ??= string.Empty;
    }

    private static void NormalizeObjectiveConfig(ContractObjectiveConfigData config)
    {
        config.AcceptTimeoutSeconds = Math.Max(0, config.AcceptTimeoutSeconds);
        config.SpawnPointTag ??= string.Empty;
        config.TargetPrototype ??= string.Empty;
        config.DeliverySpawnPrototype ??= string.Empty;
        config.StructurePrototype ??= string.Empty;
        config.GhostRolePrototype ??= string.Empty;
        config.GivePinpointer = config.GivePinpointer;
        config.PinpointerPrototype = ResolvePinpointerPrototypeId(config.PinpointerPrototype);
        config.GuardPrototype ??= string.Empty;
        config.GuardCount = Math.Max(0, config.GuardCount);
        config.RepairToolQuality = ResolveRepairToolQuality(config.RepairToolQuality);
        config.RepairDoAfterSeconds = ResolveRepairDoAfterSeconds(config.RepairDoAfterSeconds);
        config.RepairStageSound = ResolveRepairStageSound(config.RepairStageSound);
    }

    private static int GetDefaultObjectiveStageGoal(ContractExecutionKind executionKind)
    {
        return executionKind == ContractExecutionKind.RepairObjective
            ? NcContractTuning.DefaultRepairStageGoal
            : NcContractTuning.DefaultObjectiveStageGoal;
    }

    private static ContractFlowStatus ComputeContractFlowStatus(ContractServerData contract)
    {
        if (contract.Runtime.Failed)
            return ContractFlowStatus.Failed;

        if (!contract.Taken)
            return ContractFlowStatus.Available;

        if (contract.Completed)
            return ContractFlowStatus.ReadyToTurnIn;

        if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && contract.Runtime.GhostRolePendingAcceptance)
            return ContractFlowStatus.AwaitingActivation;

        return ContractFlowStatus.InProgress;
    }

    private static void SyncContractFlowStatus(ContractServerData contract)
    {
        contract.FlowStatus = ComputeContractFlowStatus(contract);
    }

    private static string ResolveObjectiveTargetId(ContractObjectiveConfigData config)
    {
        if (!string.IsNullOrWhiteSpace(config.TargetPrototype))
            return config.TargetPrototype;

        if (!string.IsNullOrWhiteSpace(config.StructurePrototype))
            return config.StructurePrototype;

        if (!string.IsNullOrWhiteSpace(config.GhostRolePrototype))
            return config.GhostRolePrototype;

        return string.Empty;
    }

    private static string ResolveTrackedObjectivePrototypeId(string? runtimePrototype, string? fallbackTargetId)
    {
        return !string.IsNullOrWhiteSpace(runtimePrototype)
            ? runtimePrototype
            : fallbackTargetId ?? string.Empty;
    }

    private static string ResolvePinpointerPrototypeId(string? prototypeId)
    {
        return string.IsNullOrWhiteSpace(prototypeId)
            ? NcContractTuning.DefaultContractPinpointerPrototypeId
            : prototypeId;
    }

    private static string ResolveRepairToolQuality(string? quality)
    {
        return string.IsNullOrWhiteSpace(quality)
            ? NcContractTuning.DefaultRepairToolQuality
            : quality;
    }

    private static float ResolveRepairDoAfterSeconds(float seconds)
    {
        if (seconds <= 0f)
            return NcContractTuning.DefaultRepairDoAfterSeconds;

        return Math.Max(NcContractTuning.MinRepairDoAfterSeconds, seconds);
    }

    private static string ResolveRepairStageSound(string? sound)
    {
        return string.IsNullOrWhiteSpace(sound)
            ? NcContractTuning.DefaultRepairStageSoundPath
            : sound;
    }

    private static void ResetContractTargetProgress(ContractServerData contract)
    {
        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            target.Progress = 0;
            targets[i] = target;
        }
    }
}

