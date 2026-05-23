using Content.Shared._NC.Trade;
using Robust.Shared.Map;


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

        if (!TryResolveContractPinpointerTarget(store, user, contractId, contract, state, out var pinpointerTarget))
            return false;

        EntityCoordinates spawnCoords;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(pinpointerTarget, out TransformComponent? targetXform))
            spawnCoords = targetXform.Coordinates;
        else
            return false;

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, config, spawnCoords);
    }

    private bool RefreshPinpointerRuntimeState(EntityUid store, string contractId, ContractServerData contract) =>
        TryGetTargetResolver(contract.ExecutionKind, out var resolver) &&
        resolver.TryRefreshPinpointerState(this, store, contractId, contract);

    private bool TryResolveContractPinpointerTarget(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        return TryGetTargetResolver(contract.ExecutionKind, out var resolver) &&
            resolver.TryResolvePinpointerTarget(this, store, user, contractId, contract, state, out target);
    }

    private bool TryResolveContractPinpointerTarget(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        return TryGetTargetResolver(contract.ExecutionKind, out var resolver) &&
            resolver.TryResolvePinpointerTarget(this, store, contractId, contract, state, out target);
    }

    private static EntityUid ResolveObjectivePinpointerTarget(
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid fallbackTarget
    )
    {
        if (contract.IsTrackedDeliveryObjective &&
            UsesTrackedDeliveryDropoff(contract) &&
            state.DeliveryDropoffEntity is { } dropoffMarker &&
            dropoffMarker != EntityUid.Invalid)
            return dropoffMarker;

        return fallbackTarget;
    }

    private static bool UsesRetrievalRouteReturnPinpointerTarget(
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
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
        out EntityUid target
    )
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
        out EntityUid target
    )
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

    private bool IsObjectiveProofCarried(EntityUid proof) =>
        TryComp(proof, out TransformComponent? xform) && IsTargetInEntityContainer(xform);
}
