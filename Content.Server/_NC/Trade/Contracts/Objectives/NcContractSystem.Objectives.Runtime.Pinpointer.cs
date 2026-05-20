using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public bool TryIssueContractPinpointer(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
            return false;

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
            return false;

        if (!contract.Taken)
            return false;

        EnsureObjectiveRuntimeDefaults(contract);
        if (contract.Runtime.Failed)
            return false;

        var config = contract.Config;
        if (!config.GivePinpointer)
            return false;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return false;

        RefreshPinpointerRuntimeState(store, contractId, contract);
        if (contract.Runtime.Failed || !_objectiveRuntime.ByContract.TryGetValue(key, out state))
            return false;

        EntityUid pinpointerTarget;
        if (!TryResolveRetrievalRouteReturnPinpointerTargetForUser(store, user, contract, state, out pinpointerTarget) &&
            UsesRetrievalSpawnedPinpointerTarget(contract))
        {
            if (!TryResolveRetrievalSpawnedPinpointerTargetForUser(store, user, contract, state, out pinpointerTarget))
                return false;
        }
        else if (pinpointerTarget == EntityUid.Invalid && contract.Completed)
        {
            if (state.ProofEntity is not { } proof || proof == EntityUid.Invalid || TerminatingOrDeleted(proof))
                return false;

            pinpointerTarget = proof;
        }
        else if (pinpointerTarget == EntityUid.Invalid)
        {
            if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && !state.GhostRoleTaken)
                return false;

            if (IsSpawnedHuntContract(contract))
            {
                if (!TryResolveSpawnedHuntPinpointerTargetForUser(store, user, contract, state, out pinpointerTarget))
                    return false;
            }
            else
            {
                if (state.TargetEntity is not { } target || target == EntityUid.Invalid || TerminatingOrDeleted(target))
                    return false;

                pinpointerTarget = ResolveObjectivePinpointerTarget(contract, state, target);
                if (pinpointerTarget == EntityUid.Invalid || TerminatingOrDeleted(pinpointerTarget))
                    return false;
            }
        }

        EntityCoordinates spawnCoords;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(pinpointerTarget, out TransformComponent? targetXform))
            spawnCoords = targetXform.Coordinates;
        else
            return false;

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, config, spawnCoords);
    }

    private bool RefreshPinpointerRuntimeState(EntityUid store, string contractId, ContractServerData contract)
    {
        return TryUpdateRetrievalRouteDeliveryProgress(store, contractId, contract);
    }

    private static EntityUid ResolveObjectivePinpointerTarget(
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid fallbackTarget)
    {
        if (contract.IsTrackedDeliveryObjective &&
            UsesTrackedDeliveryDropoff(contract) &&
            state.DeliveryDropoffEntity is { } dropoffMarker &&
            dropoffMarker != EntityUid.Invalid)
        {
            return dropoffMarker;
        }

        return fallbackTarget;
    }

    private static bool UsesRetrievalRouteReturnPinpointerTarget(
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        var config = contract.Config;
        return (contract.IsInventoryDelivery || contract.IsRetrievalRouteDelivery) &&
               contract.Completed &&
               state.ProofSpawned &&
               config.RetrievalProofEnabled &&
               config.RetrievalGuidancePinpointerEnabled &&
               config.RetrievalGuidancePinpointerTarget == NcRetrievalPinpointerTargetMode.CargoThenDestinationThenStore;
    }

    private bool TryResolveRetrievalRouteReturnPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!UsesRetrievalRouteReturnPinpointerTarget(contract, state))
            return false;

        if (state.ProofEntity is { } proof &&
            proof != EntityUid.Invalid &&
            !TerminatingOrDeleted(proof))
        {
            target = IsObjectiveProofCarried(proof) ? store : proof;
            return true;
        }

        target = store;
        return true;
    }

    private bool TryResolveRetrievalRouteReturnPinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!UsesRetrievalRouteReturnPinpointerTarget(contract, state))
            return false;

        if (state.ProofEntity is { } proof &&
            proof != EntityUid.Invalid &&
            !TerminatingOrDeleted(proof))
        {
            target = TryGetContainedEntityRoot(proof, out var proofCarrier) && proofCarrier == user
                ? store
                : proof;
            return true;
        }

        target = store;
        return true;
    }

    private bool IsObjectiveProofCarried(EntityUid proof)
    {
        return TryComp(proof, out TransformComponent? xform) && IsTargetInEntityContainer(xform);
    }
}
