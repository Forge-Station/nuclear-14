using Content.Shared._NC.Trade;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    // Shared objective lifecycle, resolution and progress.
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
    }

    private void OnObjectiveTrackedTargetResolved((EntityUid Store, string ContractId) key, EntityUid target)
    {
        _objectiveRuntimeByTarget.Remove(target);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state) && state.TargetEntity == target)
            state.TargetEntity = null;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken)
            return;

        EnsureObjectiveRuntimeDefaults(contract);
        if (EnsureContractRuntime(contract).Failed)
            return;

        switch (contract.ExecutionKind)
        {
            case ContractExecutionKind.TrackedDeliveryObjective:
                HandleTrackedDeliveryTargetResolved(key, comp, contract);
                return;

            case ContractExecutionKind.RepairObjective:
                HandleRepairObjectiveTargetResolved(key, comp, contract);
                return;

            case ContractExecutionKind.HuntObjective:
                HandleHuntObjectiveTargetResolved(key, contract);
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
        var runtime = EnsureContractRuntime(contract);
        var config = EnsureContractConfig(contract);

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
        var runtime = EnsureContractRuntime(contract);
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        runtime.Failed = false;
        runtime.FailureReason = string.Empty;
    }

    private static void ResetObjectiveState(ContractServerData contract)
    {
        var runtime = EnsureContractRuntime(contract);
        runtime.Stage = 0;
        ResetObjectiveTransientState(contract);

        contract.Required = Math.Max(1, runtime.StageGoal);
        contract.Progress = 0;
        SyncContractFlowStatus(contract);
    }

    private static void SyncObjectiveProgressFromRuntime(ContractServerData contract)
    {
        var runtime = EnsureContractRuntime(contract);
        var stageGoal = Math.Max(1, runtime.StageGoal);
        contract.Required = stageGoal;
        contract.Progress = Math.Clamp(runtime.Stage, 0, stageGoal);
        SyncContractFlowStatus(contract);
    }

    private static void SetObjectiveStage(ContractServerData contract, int stage)
    {
        var runtime = EnsureContractRuntime(contract);
        var stageGoal = Math.Max(1, runtime.StageGoal);
        runtime.Stage = Math.Clamp(stage, 0, stageGoal);
        SyncObjectiveProgressFromRuntime(contract);
    }

    private static void MarkObjectiveComplete(ContractServerData contract)
    {
        SetObjectiveStage(contract, EnsureContractRuntime(contract).StageGoal);
    }

    private static void MarkObjectiveFailed(ContractServerData contract, string failureReason)
    {
        var runtime = EnsureContractRuntime(contract);
        runtime.Failed = true;
        runtime.FailureReason = failureReason;
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        SyncContractFlowStatus(contract);
    }

    private static ContractRuntimeContextData EnsureContractRuntime(ContractServerData contract)
    {
        contract.Runtime ??= new();
        return contract.Runtime;
    }

    private static ContractObjectiveConfigData EnsureContractConfig(ContractServerData contract)
    {
        contract.Config ??= new();
        return contract.Config;
    }

    private void FinalizeObjectiveCompletion((EntityUid Store, string ContractId) key, ContractServerData contract)
    {
        MarkObjectiveComplete(contract);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            CleanupObjectivePinpointers(key, state);
    }

    private void FinalizeObjectiveFailure(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        string failureReason,
        bool deleteGuards = false)
    {
        MarkObjectiveFailed(contract, failureReason);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            CleanupObjectivePinpointers(key, state);

        FailObjectiveContract(key, comp, deleteGuards);
    }

    private void FailObjectiveContract((EntityUid Store, string ContractId) key, NcStoreComponent comp, bool deleteGuards)
    {
        CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities: true, deleteGuards: deleteGuards);
        comp.Contracts.Remove(key.ContractId);
        RefillContractsForStore(key.Store, comp, key.ContractId);
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
            RemComp<NcContractRepairObjectiveComponent>(target);
            state.TargetEntity = null;

            if (deleteTrackedEntities && !TerminatingOrDeleted(target))
                Del(target);
        }

        DeactivateTrackedDeliveryDropoff(state);

        CleanupObjectivePinpointers(key, state);

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

        if (state.ProofEntity is { } proof && !TerminatingOrDeleted(proof))
            Del(proof);

        state.ProofEntity = null;
        state.ProofSpawned = false;
        state.ProofToken = string.Empty;
        _objectiveRuntimeByContract.Remove(key);
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
        public TimeSpan? GhostRoleAcceptDeadline;
        public bool GhostRoleTaken;
        public EntityUid? ProofEntity;
        public bool ProofSpawned;
        public string ProofToken = string.Empty;
        public bool RepairInProgress;
        public EntityUid? TargetEntity;
    }
}




