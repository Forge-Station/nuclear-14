using Content.Shared._NC.Trade;
using Content.Shared.Mind;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void OnObjectiveTrackedEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (_objectiveRuntimeByTarget.TryGetValue(args.Entity, out var targetKey))
            OnObjectiveTrackedTargetResolved(targetKey, args.Entity);

        if (_objectiveRuntimeByPinpointer.TryGetValue(args.Entity, out var pinpointerKey))
            UnregisterIssuedPinpointer(args.Entity, pinpointerKey);

        if (_objectiveRuntimeByGuard.Remove(args.Entity, out var guardKey) &&
            _objectiveRuntimeByContract.TryGetValue(guardKey, out var guardState))
        {
            guardState.GuardEntities.Remove(args.Entity);
        }
        if (_objectiveRuntimeByProof.Remove(args.Entity, out var proofKey))
            OnObjectiveTrackedProofDestroyed(proofKey, args.Entity);

        if (_objectiveRuntimeByRetrievalCargo.Remove(args.Entity, out var retrievalCargoKey))
            OnRetrievalSpawnedCargoDestroyed(retrievalCargoKey, args.Entity);

        TryHandleHuntBodyEntityTerminating(args.Entity);
    }

    private void OnObjectiveTrackedProofDestroyed(
        (EntityUid Store, string ContractId) key,
        EntityUid proof)
    {
        if (_objectiveRuntimeByContract.TryGetValue(key, out var state) && state.ProofEntity == proof)
        {
            state.ProofEntity = null;
            state.ProofSpawned = false;
        }

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken)
            return;

        if (contract.Runtime.Failed)
            return;

        Sawmill.Info(
            $"[Contracts] Proof for '{key.ContractId}' destroyed externally on {ToPrettyString(key.Store)}; failing contract.");

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-proof-destroyed"),
            deleteGuards: false);
    }

    private void OnObjectiveTrackedTargetResolved((EntityUid Store, string ContractId) key, EntityUid target)
    {
        _objectiveRuntimeByTarget.Remove(target);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state) && state.TargetEntity == target)
        {
            state.TargetEntity = null;
            if (TryComp(target, out TransformComponent? targetXform))
                state.LastKnownTargetCoordinates = targetXform.Coordinates;
        }

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken)
            return;

        EnsureObjectiveRuntimeDefaults(contract);
        if (contract.Runtime.Failed)
            return;

        switch (contract.ExecutionKind)
        {
            case ContractExecutionKind.TrackedDeliveryObjective:
                HandleTrackedDeliveryTargetResolved(key, comp, contract);
                return;

            case ContractExecutionKind.HuntObjective:
                HandleHuntObjectiveTargetResolved(key, comp, contract);
                return;

            case ContractExecutionKind.GhostRoleObjective:
                HandleGhostRoleTargetResolved(key, comp, contract);
                return;

            default:
                return;
        }
    }

    private static void EnsureObjectiveRuntimeDefaults(ContractServerData contract)
    {
        var runtime = contract.Runtime;
        var config = contract.Config;

        NormalizeRuntimeState(contract.ExecutionKind, runtime);
        NormalizeObjectiveConfig(config);

        if (!contract.UsesStageObjectiveProgress)
        {
            SyncContractFlowStatus(contract);
            return;
        }

        SyncObjectiveProgressFromRuntime(contract);

        if (string.IsNullOrWhiteSpace(contract.TargetItem))
            contract.TargetItem = ResolveObjectiveTargetId(config);

        SyncContractFlowStatus(contract);
    }

    private static void ResetObjectiveTransientState(ContractServerData contract)
    {
        var runtime = contract.Runtime;
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        runtime.GhostRoleSurvivalRemainingSeconds = 0;
        runtime.Failed = false;
        runtime.Outcome = ContractObjectiveOutcome.None;
        runtime.FailureReason = string.Empty;
        runtime.StatusHint = string.Empty;
    }

    private static void ResetObjectiveState(ContractServerData contract)
    {
        var runtime = contract.Runtime;
        runtime.Stage = 0;
        ResetObjectiveTransientState(contract);

        contract.Required = Math.Max(1, runtime.StageGoal);
        contract.Progress = 0;
        SyncContractFlowStatus(contract);
    }

    private static void SyncObjectiveProgressFromRuntime(ContractServerData contract)
    {
        var runtime = contract.Runtime;
        var stageGoal = Math.Max(1, runtime.StageGoal);
        contract.Required = stageGoal;
        contract.Progress = Math.Clamp(runtime.Stage, 0, stageGoal);
        SyncContractFlowStatus(contract);
    }

    private static void SetObjectiveStage(ContractServerData contract, int stage)
    {
        var runtime = contract.Runtime;
        var stageGoal = Math.Max(1, runtime.StageGoal);
        runtime.Stage = Math.Clamp(stage, 0, stageGoal);
        SyncObjectiveProgressFromRuntime(contract);
    }

    private static void MarkObjectiveComplete(ContractServerData contract)
    {
        contract.Runtime.Outcome = ContractObjectiveOutcome.Success;
        SetObjectiveStage(contract, contract.Runtime.StageGoal);
    }

    private static void MarkObjectiveFailed(
        ContractServerData contract,
        string failureReason,
        ContractObjectiveOutcome outcome = ContractObjectiveOutcome.Failed)
    {
        var runtime = contract.Runtime;
        runtime.Failed = true;
        runtime.Outcome = outcome;
        runtime.FailureReason = failureReason;
        runtime.StatusHint = failureReason;
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        runtime.GhostRoleSurvivalRemainingSeconds = 0;
        SyncContractFlowStatus(contract);
    }


    private void FinalizeObjectiveCompletion((EntityUid Store, string ContractId) key, ContractServerData contract)
    {
        MarkObjectiveComplete(contract);

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        if (state.ProofEntity is { } proof && proof != EntityUid.Invalid && !TerminatingOrDeleted(proof))
        {
            RetargetObjectivePinpointers(key, state, proof);
            return;
        }

        if (RequiresSpawnedHuntBodyTurnIn(contract) && TryGetHuntBodyEntity(state, out var body))
        {
            RetargetObjectivePinpointers(key, state, body);
            return;
        }

        CleanupObjectivePinpointers(key, state);
    }

    private void FinalizeObjectiveTerminalOutcome(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        string failureReason,
        ContractObjectiveOutcome outcome = ContractObjectiveOutcome.Failed,
        bool deleteTrackedEntities = true,
        bool deleteGuards = false)
    {
        MarkObjectiveFailed(contract, failureReason, outcome);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            CleanupObjectivePinpointers(key, state);

        FailObjectiveContract(key, comp, deleteTrackedEntities, deleteGuards);
    }

    private void FailObjectiveContract(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        bool deleteTrackedEntities,
        bool deleteGuards)
    {
        CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities, deleteGuards);
        comp.Contracts.Remove(key.ContractId);
        RefillContractsForStore(key.Store, comp, key.ContractId);

        var ev = new NcContractsChangedEvent();
        RaiseLocalEvent(key.Store, ref ev);
    }

    private void CleanupObjectiveRuntime(
        EntityUid store,
        string contractId,
        bool deleteTrackedEntities,
        bool deleteGuards = true
    )
    {
        var key = (store, contractId);

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is { } target)
        {
            _objectiveRuntimeByTarget.Remove(target);
            state.TargetEntity = null;

            if (deleteTrackedEntities && !TerminatingOrDeleted(target))
                Del(target);
        }

        DeactivateTrackedDeliveryDropoff(key, state);

        CleanupRetrievalSpawnedEntities(state, deleteTrackedEntities);
        CleanupSpawnedHuntBodyTarget(state, deleteTrackedEntities);
        CleanupHuntSpawnedTargets(state, deleteTrackedEntities);

        CleanupObjectivePinpointers(key, state);
        CleanupGhostRoleSurvivalObjective(state);

        if (state.GuardEntities.Count > 0)
        {
            for (var i = 0; i < state.GuardEntities.Count; i++)
            {
                var guard = state.GuardEntities[i];
                _objectiveRuntimeByGuard.Remove(guard);

                if (deleteGuards && !TerminatingOrDeleted(guard))
                    Del(guard);
            }

            state.GuardEntities.Clear();
        }

        if (state.ProofEntity is { } proof)
        {
            _objectiveRuntimeByProof.Remove(proof);

            if (!TerminatingOrDeleted(proof))
                Del(proof);
        }

        state.ProofEntity = null;
        state.ProofSpawned = false;
        state.ProofToken = string.Empty;

        if (state.HuntActive)
        {
            state.HuntActive = false;
            _activeHuntObjectives.Remove(key);
        }

        if (state.RetrievalRouteDeliveryActive)
        {
            state.RetrievalRouteDeliveryActive = false;
            _activeRetrievalRouteDeliveries.Remove(key);
        }

        _activeGhostRoleObjectives.Remove(key);

        state.RetrievalDeliveredEntities.Clear();
        state.RetrievalAcceptedCargoCount = 0;
        state.RetrievalLastAcceptedCargoCoordinates = null;
        state.RetrievalRouteDeliveryCompleted = false;
        state.HuntTargetWasKilled = false;
        state.GhostRoleSurvivalStart = null;
        state.GhostRoleSurvivalDeadline = null;
        state.GhostRoleSurvivalMind = null;
        state.GhostRoleSurvivalObjective = null;
        state.GhostRoleSurvivalSucceeded = false;
        state.LastKnownTargetCoordinates = null;
        _objectiveRuntimeByContract.Remove(key);
    }

    private void CleanupGhostRoleSurvivalObjective(ObjectiveRuntimeState state)
    {
        if (state.GhostRoleSurvivalObjective is not { } objective || objective == EntityUid.Invalid)
            return;

        if (state.GhostRoleSurvivalSucceeded)
        {
            if (!TerminatingOrDeleted(objective) &&
                TryComp(objective, out NcContractGhostRoleSurvivalObjectiveComponent? survival))
            {
                survival.Finished = true;
                survival.Succeeded = true;
            }

            return;
        }

        if (state.GhostRoleSurvivalMind is { } mindId &&
            TryComp(mindId, out MindComponent? mind))
        {
            mind.Objectives.Remove(objective);
        }

        if (!TerminatingOrDeleted(objective))
            Del(objective);
    }

    private void CleanupRetrievalSpawnedEntities(ObjectiveRuntimeState state, bool deleteSpawnedEntities)
    {
        if (state.RetrievalSpawnedEntities.Count == 0)
        {
            state.RetrievalSpawnedEntitySet.Clear();
            return;
        }

        for (var i = state.RetrievalSpawnedEntities.Count - 1; i >= 0; i--)
        {
            var ent = state.RetrievalSpawnedEntities[i];
            _objectiveRuntimeByRetrievalCargo.Remove(ent);

            if (deleteSpawnedEntities && ent != EntityUid.Invalid && !TerminatingOrDeleted(ent))
                Del(ent);
        }

        state.RetrievalSpawnedEntities.Clear();
        state.RetrievalSpawnedEntitySet.Clear();
    }

    private void CleanupHuntSpawnedTargets(ObjectiveRuntimeState state, bool deleteSpawnedTargets)
    {
        if (state.HuntSpawnedTargets.Count == 0)
            return;

        for (var i = state.HuntSpawnedTargets.Count - 1; i >= 0; i--)
        {
            var ent = state.HuntSpawnedTargets[i];
            if (deleteSpawnedTargets && ent != EntityUid.Invalid && !TerminatingOrDeleted(ent))
                Del(ent);
        }

        state.HuntSpawnedTargets.Clear();
    }

    private void CleanupSpawnedHuntBodyTarget(ObjectiveRuntimeState state, bool deleteBody)
    {
        if (state.HuntBodyEntity is not { } body || body == EntityUid.Invalid)
            return;

        state.HuntBodyEntity = null;
        RemoveSpawnedHuntTarget(state, body);

        if (deleteBody && !TerminatingOrDeleted(body))
            Del(body);
    }

    private static bool IsTargetInEntityContainer(TransformComponent xform)
    {
        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid)
            return false;

        if (xform.MapUid is { } mapUid && parent == mapUid)
            return false;

        if (xform.GridUid is { } gridUid && parent == gridUid)
            return false;

        return true;
    }

    private void UpdateObjectiveContractProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        switch (contract.ExecutionKind)
        {
            case ContractExecutionKind.HuntObjective:
                SyncHuntObjectiveProgress(store, contractId, contract);
                SyncObjectiveProgressFromRuntime(contract);
                if (IsSpawnedHuntContract(contract))
                    return;
                break;

            case ContractExecutionKind.GhostRoleObjective:
                SyncGhostRoleObjectiveProgress(store, contractId, contract);
                break;
        }

        SyncObjectiveProgressFromRuntime(contract);
        ResetContractTargetProgress(contract);
        SyncContractFlowStatus(contract);
    }

    private sealed class ObjectiveRuntimeState
    {
        public bool ActiveDeliveryDropoff;
        public bool DeliveryDropoffCompleted;
        public MapCoordinates? DeliveryDropoffCoordinates;
        public EntityUid? DeliveryDropoffEntity;
        public readonly List<EntityUid> GuardEntities = new();
        public readonly HashSet<EntityUid> PinpointerEntities = new();
        public readonly List<EntityUid> RetrievalSpawnedEntities = new();
        public readonly HashSet<EntityUid> RetrievalSpawnedEntitySet = new();
        public readonly List<EntityUid> HuntSpawnedTargets = new();
        public readonly HashSet<EntityUid> RetrievalDeliveredEntities = new();
        public int RetrievalAcceptedCargoCount;
        public EntityCoordinates? RetrievalLastAcceptedCargoCoordinates;
        public bool RetrievalRouteDeliveryActive;
        public bool RetrievalRouteDeliveryCompleted;
        public EntityCoordinates? RetrievalDeliveryCoordinates;
        public TimeSpan? GhostRoleAcceptDeadline;
        public TimeSpan? GhostRoleSurvivalStart;
        public TimeSpan? GhostRoleSurvivalDeadline;
        public EntityUid? GhostRoleSurvivalMind;
        public EntityUid? GhostRoleSurvivalObjective;
        public long GhostRoleRoundEndId;
        public bool GhostRoleSurvivalSucceeded;
        public bool GhostRoleTaken;
        public bool HuntTargetWasKilled;
        public bool HuntActive;
        public EntityUid? HuntBodyEntity;
        public EntityCoordinates? LastKnownTargetCoordinates;
        public EntityUid? ProofEntity;
        public bool ProofSpawned;
        public string ProofToken = string.Empty;
        public EntityUid? TargetEntity;
    }
}
