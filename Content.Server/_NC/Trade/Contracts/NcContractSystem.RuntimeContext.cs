using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private static void NormalizeRuntimeState(ContractRuntimeContextData runtime)
    {
        runtime.StageGoal = runtime.StageGoal > 0
            ? runtime.StageGoal
            : NcContractTuning.DefaultObjectiveStageGoal;
        runtime.Stage = Math.Clamp(runtime.Stage, 0, runtime.StageGoal);
        runtime.AcceptTimeoutRemainingSeconds = Math.Max(0, runtime.AcceptTimeoutRemainingSeconds);
        runtime.GhostRoleSurvivalRemainingSeconds = Math.Max(0, runtime.GhostRoleSurvivalRemainingSeconds);
    }

    private static void NormalizeObjectiveConfig(ContractObjectiveConfigData config)
    {
        config.AcceptTimeoutSeconds = Math.Max(0, config.AcceptTimeoutSeconds);
        config.SpawnPoint = NormalizeContractPointSelector(config.SpawnPoint, true);
        config.DropoffPoint = NormalizeContractPointSelector(config.DropoffPoint, false);
        config.GhostRoleSurvivalDurationSeconds = NormalizePositiveOrDefault(
            config.GhostRoleSurvivalDurationSeconds,
            NcGhostRoleSurvivalData.DefaultDurationSeconds);
        config.PinpointerPrototype = ResolvePinpointerPrototypeId(config.PinpointerPrototype);
        config.GuardCount = Math.Max(0, config.GuardCount);

        NormalizeRetrievalSpawnConfig(config);
        config.RetrievalDestinationRadius = Math.Max(0.25f, config.RetrievalDestinationRadius);
        config.RetrievalDestinationPoint = NormalizeContractPointSelector(config.RetrievalDestinationPoint, false);

        NormalizeRetrievalClaimConfig(config);
        config.RetrievalGuidancePinpointerPrototype =
            ResolvePinpointerPrototypeId(config.RetrievalGuidancePinpointerPrototype);
        config.RetrievalGuidanceMaxActivePinpointers = Math.Max(0, config.RetrievalGuidanceMaxActivePinpointers);

        RemoveBlankStrings(config.SpawnSpecific);
    }

    private static int NormalizePositiveOrDefault(int value, int fallback) =>
        value > 0 ? value : fallback;

    private static void NormalizeRetrievalSpawnConfig(ContractObjectiveConfigData config)
    {
        if (!config.RetrievalSpawnEnabled)
        {
            ClearRetrievalSpawnConfig(config);
            return;
        }

        config.RetrievalSpawnPoint = NormalizeContractPointSelector(
            config.RetrievalSpawnPoint,
            config.RetrievalSpawnFallbackToStore);

        if (config.RetrievalSpawnPoint != null)
            return;

        config.RetrievalSpawnEnabled = false;
        config.RetrievalRequireSpawnedEntities = false;
    }

    private static void ClearRetrievalSpawnConfig(ContractObjectiveConfigData config)
    {
        config.RetrievalSpawnPoint = null;
        config.RetrievalSpawnFallbackToStore = false;
        config.RetrievalRequireSpawnedEntities = false;
    }

    private static void NormalizeRetrievalClaimConfig(ContractObjectiveConfigData config)
    {
        if (config.RetrievalClaimMode != NcRetrievalClaimMode.StoreCargo)
            return;

        config.RetrievalProofEnabled = false;
        config.RetrievalProofConsumeOnRewardClaim = false;
        config.RetrievalProofOwnership = NcRetrievalProofOwnership.Bearer;
        config.RetrievalProofReissue = NcRetrievalProofReissuePolicy.Never;

        if (!string.IsNullOrWhiteSpace(config.RetrievalRouteId))
            config.ProofPrototype = string.Empty;
    }

    private static void RemoveBlankStrings(List<string> values)
    {
        for (var i = values.Count - 1; i >= 0; i--)
            if (string.IsNullOrWhiteSpace(values[i]))
                values.RemoveAt(i);
    }

    private static ContractPointSelectorPrototype? CloneContractPointSelector(ContractPointSelectorPrototype? selector)
    {
        if (selector == null)
            return null;

        var sourceOptions = selector.Options;
        var clone = new ContractPointSelectorPrototype
        {
            Type = selector.Type,
            Id = selector.Id,
            Options = new(sourceOptions.Count)
        };

        for (var i = 0; i < sourceOptions.Count; i++)
            clone.Options.Add(sourceOptions[i]);

        return clone;
    }

    private static ContractPointSelectorPrototype? NormalizeContractPointSelector(
        ContractPointSelectorPrototype? selector,
        bool defaultToStore
    )
    {
        if (selector == null)
            return defaultToStore ? new ContractPointSelectorPrototype() : null;

        return selector.Type switch
        {
            ContractPointSelectorType.Store => NormalizeStorePointSelector(selector),
            ContractPointSelectorType.MarkerId or ContractPointSelectorType.MarkerGroup =>
                NormalizeNamedPointSelector(selector, defaultToStore),
            ContractPointSelectorType.Weighted => NormalizeWeightedPointSelector(selector, defaultToStore),
            _ => GetFallbackPointSelector(defaultToStore)
        };
    }

    private static ContractPointSelectorPrototype NormalizeStorePointSelector(ContractPointSelectorPrototype selector)
    {
        selector.Id = string.Empty;
        selector.Options.Clear();
        return selector;
    }

    private static ContractPointSelectorPrototype? NormalizeNamedPointSelector(
        ContractPointSelectorPrototype selector,
        bool defaultToStore
    )
    {
        selector.Options.Clear();
        return !string.IsNullOrWhiteSpace(selector.Id)
            ? selector
            : GetFallbackPointSelector(defaultToStore);
    }

    private static ContractPointSelectorPrototype? NormalizeWeightedPointSelector(
        ContractPointSelectorPrototype selector,
        bool defaultToStore
    )
    {
        RemoveInvalidPointOptions(selector.Options);
        selector.Id = string.Empty;
        return selector.Options.Count > 0
            ? selector
            : GetFallbackPointSelector(defaultToStore);
    }

    private static ContractPointSelectorPrototype? GetFallbackPointSelector(bool defaultToStore) =>
        defaultToStore ? new ContractPointSelectorPrototype() : null;

    private static void RemoveInvalidPointOptions(List<WeightedContractPointOptionEntry> options)
    {
        for (var i = options.Count - 1; i >= 0; i--)
            if (!IsContractPointOptionUsable(options[i]))
                options.RemoveAt(i);
    }

    private static bool IsContractPointOptionUsable(in WeightedContractPointOptionEntry option) =>
        option.Weight > 0 && option.Type switch
        {
            ContractPointSelectorType.Store => true,
            ContractPointSelectorType.MarkerId or ContractPointSelectorType.MarkerGroup =>
                !string.IsNullOrWhiteSpace(option.Id),
            _ => false
        };

    private static ContractFlowStatus ComputeContractFlowStatus(ContractServerData contract)
    {
        var runtime = contract.Runtime;

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

    private static void SyncContractFlowStatus(ContractServerData contract) =>
        contract.FlowStatus = ComputeContractFlowStatus(contract);

    private static string ResolveObjectiveTargetId(ContractObjectiveConfigData config)
    {
        if (!string.IsNullOrWhiteSpace(config.TargetPrototype))
            return config.TargetPrototype;

        if (!string.IsNullOrWhiteSpace(config.GhostRolePrototype))
            return config.GhostRolePrototype;

        return string.Empty;
    }

    private static string ResolveTrackedObjectivePrototypeId(string? runtimePrototype, string? fallbackTargetId) =>
        !string.IsNullOrWhiteSpace(runtimePrototype)
            ? runtimePrototype
            : fallbackTargetId ?? string.Empty;

    private static string ResolvePinpointerPrototypeId(string? prototypeId) =>
        string.IsNullOrWhiteSpace(prototypeId)
            ? NcContractTuning.DefaultContractPinpointerPrototypeId
            : prototypeId;

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
