using Content.Shared._NC.Trade;
using Content.Shared.Customization.Systems;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static void NormalizeRuntimeState(ContractExecutionKind executionKind, ContractRuntimeContextData runtime)
    {
        runtime.StageGoal = runtime.StageGoal > 0
            ? runtime.StageGoal
            : GetDefaultObjectiveStageGoal(executionKind);
        runtime.Stage = Math.Clamp(runtime.Stage, 0, runtime.StageGoal);
        runtime.AcceptTimeoutRemainingSeconds = Math.Max(0, runtime.AcceptTimeoutRemainingSeconds);
        runtime.GhostRoleSurvivalRemainingSeconds = Math.Max(0, runtime.GhostRoleSurvivalRemainingSeconds);
        runtime.FailureReason ??= string.Empty;
        runtime.StatusHint ??= string.Empty;
    }

    private static void NormalizeObjectiveConfig(ContractObjectiveConfigData config)
    {
        config.AcceptTimeoutSeconds = Math.Max(0, config.AcceptTimeoutSeconds);
        config.SpawnPoint = NormalizeContractPointSelector(config.SpawnPoint, defaultToStore: true);
        config.DropoffPoint = NormalizeContractPointSelector(config.DropoffPoint, defaultToStore: false);

        config.TargetPrototype ??= string.Empty;
        config.DeliverySpawnPrototype ??= string.Empty;
        config.StructurePrototype ??= string.Empty;
        config.GhostRole ??= string.Empty;
        config.ProofPrototype ??= string.Empty;
        config.GhostRolePrototype ??= string.Empty;
        config.GhostRoleName ??= string.Empty;
        config.GhostRoleDescription ??= string.Empty;
        config.GhostRoleRules ??= string.Empty;
        config.GhostRoleRequirements ??= new List<CharacterRequirement>();
        config.GhostRoleCharacterName ??= string.Empty;
        config.GhostRoleCharacterHair ??= string.Empty;
        config.GhostRolePerks ??= new List<string>();
        if (config.GhostRoleSurvivalDurationSeconds <= 0)
            config.GhostRoleSurvivalDurationSeconds = NcGhostRoleSurvivalData.DefaultDurationSeconds;
        config.GhostRoleSurvivalBriefing ??= string.Empty;
        config.GhostRoleSurvivalObjectiveTitle ??= string.Empty;
        config.GhostRoleSurvivalObjectiveDescription ??= string.Empty;
        config.GivePinpointer = config.GivePinpointer;
        config.PinpointerPrototype = ResolvePinpointerPrototypeId(config.PinpointerPrototype);
        config.GuardPrototype ??= string.Empty;
        config.GuardCount = Math.Max(0, config.GuardCount);
        config.RepairToolQuality = ResolveRepairToolQuality(config.RepairToolQuality);
        config.RepairDoAfterSeconds = ResolveRepairDoAfterSeconds(config.RepairDoAfterSeconds);
        config.RepairStageSound = ResolveRepairStageSound(config.RepairStageSound);
        config.HuntV2BodyPrototype ??= string.Empty;
        if (config.RetrievalSpawnEnabled)
        {
            config.RetrievalSpawnPoint = NormalizeContractPointSelector(
                config.RetrievalSpawnPoint,
                defaultToStore: config.RetrievalSpawnFallbackToStore);

            if (config.RetrievalSpawnPoint == null)
            {
                config.RetrievalSpawnEnabled = false;
                config.RetrievalRequireSpawnedEntities = false;
            }
        }
        else
        {
            config.RetrievalSpawnPoint = null;
            config.RetrievalSpawnFallbackToStore = false;
            config.RetrievalRequireSpawnedEntities = false;
        }

        config.RetrievalRouteId ??= string.Empty;
        config.RetrievalDestinationId ??= string.Empty;
        config.RetrievalDestinationRadius = Math.Max(0.25f, config.RetrievalDestinationRadius);
        config.RetrievalDestinationPoint = NormalizeContractPointSelector(config.RetrievalDestinationPoint, defaultToStore: false);

        if (config.RetrievalClaimMode == NcRetrievalClaimMode.StoreCargo)
        {
            config.RetrievalProofEnabled = false;
            config.RetrievalProofConsumeOnRewardClaim = false;
            config.RetrievalProofOwnership = NcRetrievalProofOwnership.Bearer;
            config.RetrievalProofReissue = NcRetrievalProofReissuePolicy.Never;

            if (!string.IsNullOrWhiteSpace(config.RetrievalRouteId))
                config.ProofPrototype = string.Empty;
        }

        config.RetrievalGuidancePinpointerPrototype = ResolvePinpointerPrototypeId(config.RetrievalGuidancePinpointerPrototype);
        config.RetrievalGuidanceMaxActivePinpointers = Math.Max(0, config.RetrievalGuidanceMaxActivePinpointers);
        config.RetrievalSourceHint ??= string.Empty;
        config.RetrievalDestinationHint ??= string.Empty;

        config.SpawnSpecific ??= new List<string>();
        for (var i = config.SpawnSpecific.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(config.SpawnSpecific[i]))
                config.SpawnSpecific.RemoveAt(i);
        }

        for (var i = config.GhostRoleRequirements.Count - 1; i >= 0; i--)
        {
            if (config.GhostRoleRequirements[i] is null)
                config.GhostRoleRequirements.RemoveAt(i);
        }
    }

    private static ContractPointSelectorPrototype? CloneContractPointSelector(ContractPointSelectorPrototype? selector)
    {
        if (selector == null)
            return null;

        var sourceOptions = selector.Options ?? new List<WeightedContractPointOptionEntry>();

        var clone = new ContractPointSelectorPrototype
        {
            Type = selector.Type,
            Id = selector.Id ?? string.Empty,
            Options = new List<WeightedContractPointOptionEntry>(sourceOptions.Count)
        };

        for (var i = 0; i < sourceOptions.Count; i++)
            clone.Options.Add(sourceOptions[i]);

        return clone;
    }

    private static ContractPointSelectorPrototype? NormalizeContractPointSelector(
        ContractPointSelectorPrototype? selector,
        bool defaultToStore)
    {
        if (selector == null)
            return defaultToStore ? new ContractPointSelectorPrototype() : null;

        selector.Id ??= string.Empty;
        selector.Options ??= new List<WeightedContractPointOptionEntry>();

        switch (selector.Type)
        {
            case ContractPointSelectorType.Store:
                selector.Id = string.Empty;
                selector.Options.Clear();
                return selector;

            case ContractPointSelectorType.MarkerId:
            case ContractPointSelectorType.MarkerGroup:
                selector.Options.Clear();
                if (!string.IsNullOrWhiteSpace(selector.Id))
                    return selector;
                return defaultToStore ? new ContractPointSelectorPrototype() : null;

            case ContractPointSelectorType.Weighted:
                for (var i = selector.Options.Count - 1; i >= 0; i--)
                {
                    var option = selector.Options[i];
                    if (option.Weight <= 0 || !IsContractPointOptionUsable(option))
                        selector.Options.RemoveAt(i);
                }

                selector.Id = string.Empty;
                if (selector.Options.Count > 0)
                    return selector;
                return defaultToStore ? new ContractPointSelectorPrototype() : null;

            default:
                return defaultToStore ? new ContractPointSelectorPrototype() : null;
        }
    }

    private static bool IsContractPointOptionUsable(in WeightedContractPointOptionEntry option)
    {
        return option.Type switch
        {
            ContractPointSelectorType.Store => true,
            ContractPointSelectorType.MarkerId or ContractPointSelectorType.MarkerGroup => !string.IsNullOrWhiteSpace(option.Id),
            _ => false
        };
    }

    private static int GetDefaultObjectiveStageGoal(ContractExecutionKind executionKind)
    {
        return executionKind == ContractExecutionKind.RepairObjective
            ? NcContractTuning.DefaultRepairStageGoal
            : NcContractTuning.DefaultObjectiveStageGoal;
    }

    private static ContractFlowStatus ComputeContractFlowStatus(ContractServerData contract)
    {
        var runtime = contract.Runtime ??= new ContractRuntimeContextData();

        if (runtime.Failed)
            return ContractFlowStatus.Failed;

        if (!contract.Taken)
            return ContractFlowStatus.Available;

        if (contract.Completed)
            return ContractFlowStatus.ReadyToTurnIn;

        if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && runtime.GhostRolePendingAcceptance)
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
