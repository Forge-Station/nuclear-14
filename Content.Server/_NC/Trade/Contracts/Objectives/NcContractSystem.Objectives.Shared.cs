using Content.Shared._NC.Trade;
using Content.Shared.Mind;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void OnObjectiveTrackedEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (_objectiveRuntime.ByTarget.TryGetValue(args.Entity, out var targetKey))
            OnObjectiveTrackedTargetResolved(targetKey, args.Entity);

        if (_objectiveRuntime.ByPinpointer.TryGetValue(args.Entity, out var pinpointerKey))
            UnregisterIssuedPinpointer(args.Entity, pinpointerKey);

        if (_objectiveRuntime.ByGuard.Remove(args.Entity, out var guardKey) &&
            _objectiveRuntime.ByContract.TryGetValue(guardKey, out var guardState))
        {
            guardState.GuardEntities.Remove(args.Entity);
        }
        if (_objectiveRuntime.ByProof.Remove(args.Entity, out var proofKey))
            OnObjectiveTrackedProofDestroyed(proofKey, args.Entity);

        if (_objectiveRuntime.ByRetrievalCargo.Remove(args.Entity, out var retrievalCargoKey))
            OnRetrievalSpawnedCargoDestroyed(retrievalCargoKey, args.Entity);

        TryHandleHuntBodyEntityTerminating(args.Entity);
    }

    private void OnObjectiveTrackedProofDestroyed(
        (EntityUid Store, string ContractId) key,
        EntityUid proof)
    {
        if (_objectiveRuntime.ByContract.TryGetValue(key, out var state) && state.ProofEntity == proof)
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
        _objectiveRuntime.ByTarget.Remove(target);

        if (_objectiveRuntime.ByContract.TryGetValue(key, out var state) && state.TargetEntity == target)
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

        if (TryGetObjectiveHandler(contract.ExecutionKind, out var handler))
            handler.OnTrackedTargetResolved(this, key, comp, contract);
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
}
