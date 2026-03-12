using Content.Shared._NC.Trade;
namespace Content.Server._NC.Trade;
public sealed partial class NcContractSystem : EntitySystem
{
    private const int DefaultObjectiveStageGoal = 1;
    private const int DefaultRepairStageGoal = 3;
    private const float MinRepairDoAfterSeconds = 0.1f;
    private const string DefaultContractPinpointerPrototypeId = "PinpointerUniversal";
    private const string DefaultRepairToolQuality = "Welding";
    private const float DefaultRepairDoAfterSeconds = 2f;
    private const string DefaultRepairStageSoundPath = "/Audio/Effects/sparks4.ogg";
    private static ContractRuntimeContextData CreateInitialRuntimeContext(StoreContractPrototype proto)
    {
        var runtimeProto = proto.Runtime;
        var runtime = new ContractRuntimeContextData
        {
            Stage = 0,
            StageGoal = runtimeProto.StageGoal,
            AcceptTimeoutSeconds = runtimeProto.AcceptTimeoutSeconds,
            Failed = false,
            FailureReason = string.Empty,
            SpawnPointTag = runtimeProto.SpawnPointTag ?? string.Empty,
            TargetPrototype = runtimeProto.TargetPrototype ?? string.Empty,
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
        NormalizeRuntimeContext(proto.ObjectiveType, runtime);
        return runtime;
    }
    private static void NormalizeRuntimeContext(ContractObjectiveType objectiveType, ContractRuntimeContextData runtime)
    {
        runtime.StageGoal = runtime.StageGoal > 0
            ? runtime.StageGoal
            : GetDefaultObjectiveStageGoal(objectiveType);
        runtime.AcceptTimeoutSeconds = Math.Max(0, runtime.AcceptTimeoutSeconds);
        runtime.GuardCount = Math.Max(0, runtime.GuardCount);
        runtime.Stage = Math.Clamp(runtime.Stage, 0, runtime.StageGoal);
        runtime.SpawnPointTag ??= string.Empty;
        runtime.TargetPrototype ??= string.Empty;
        runtime.StructurePrototype ??= string.Empty;
        runtime.GhostRolePrototype ??= string.Empty;
        runtime.PinpointerPrototype = ResolvePinpointerPrototypeId(runtime.PinpointerPrototype);
        runtime.GuardPrototype ??= string.Empty;
        runtime.RepairToolQuality = ResolveRepairToolQuality(runtime.RepairToolQuality);
        runtime.RepairDoAfterSeconds = ResolveRepairDoAfterSeconds(runtime.RepairDoAfterSeconds);
        runtime.RepairStageSound = ResolveRepairStageSound(runtime.RepairStageSound);
        runtime.FailureReason ??= string.Empty;
    }
    private static int GetDefaultObjectiveStageGoal(ContractObjectiveType objectiveType)
    {
        return objectiveType == ContractObjectiveType.Repair
            ? DefaultRepairStageGoal
            : DefaultObjectiveStageGoal;
    }
    private static string ResolveObjectiveTargetId(ContractRuntimeContextData runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.TargetPrototype))
            return runtime.TargetPrototype;
        if (!string.IsNullOrWhiteSpace(runtime.StructurePrototype))
            return runtime.StructurePrototype;
        if (!string.IsNullOrWhiteSpace(runtime.GhostRolePrototype))
            return runtime.GhostRolePrototype;
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
            ? DefaultContractPinpointerPrototypeId
            : prototypeId;
    }
    private static string ResolveRepairToolQuality(string? quality)
    {
        return string.IsNullOrWhiteSpace(quality)
            ? DefaultRepairToolQuality
            : quality;
    }
    private static float ResolveRepairDoAfterSeconds(float seconds)
    {
        if (seconds <= 0f)
            return DefaultRepairDoAfterSeconds;
        return Math.Max(MinRepairDoAfterSeconds, seconds);
    }
    private static string ResolveRepairStageSound(string? sound)
    {
        return string.IsNullOrWhiteSpace(sound)
            ? DefaultRepairStageSoundPath
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