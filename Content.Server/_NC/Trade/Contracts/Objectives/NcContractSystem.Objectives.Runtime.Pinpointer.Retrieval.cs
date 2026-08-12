using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private static bool UsesRetrievalSpawnedPinpointerTarget(ContractServerData contract) =>
        UsesRetrievalSpawnedCargoSupport(contract);

    private bool TryResolveRetrievalSpawnedPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        if (!UsesRetrievalSpawnedPinpointerTarget(contract))
            return false;

        if (contract.Completed &&
            contract.Config.RetrievalDestinationType == NcRetrievalDestinationTargetType.StoreUi)
        {
            target = store;
            return true;
        }

        PruneRetrievalSpawnedEntities(state);

        if (TryResolveRetrievalStoreReturnPinpointerTarget(store, contract, state, out target))
            return true;

        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var candidate = state.RetrievalSpawnedEntities[i];
            if (!TryResolveRetrievalCarriedCargoPinpointerTarget(store, contract, state, candidate, out target))
                continue;

            return true;
        }

        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var candidate = state.RetrievalSpawnedEntities[i];
            if (candidate == EntityUid.Invalid || TerminatingOrDeleted(candidate))
                continue;

            if (IsRetrievalCargoAlreadyAtPinpointerDestination(store, contract, state, candidate))
                continue;

            target = candidate;
            return true;
        }

        return false;
    }

    private bool TryResolveRetrievalSpawnedPinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        if (!UsesRetrievalSpawnedPinpointerTarget(contract))
            return false;

        if (contract.Completed &&
            contract.Config.RetrievalDestinationType == NcRetrievalDestinationTargetType.StoreUi)
        {
            target = store;
            return true;
        }

        PruneRetrievalSpawnedEntities(state);

        if (TryResolveRetrievalStoreReturnPinpointerTarget(store, contract, state, out target))
            return true;

        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var candidate = state.RetrievalSpawnedEntities[i];
            if (!IsRetrievalCargoControlledByUser(candidate, user))
                continue;

            if (!TryResolveRetrievalControlledCargoPinpointerTarget(store, contract, state, candidate, out target))
                continue;

            return true;
        }

        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var candidate = state.RetrievalSpawnedEntities[i];
            if (candidate == EntityUid.Invalid || TerminatingOrDeleted(candidate))
                continue;

            if (IsRetrievalCargoAlreadyAtPinpointerDestination(store, contract, state, candidate))
                continue;

            target = candidate;
            return true;
        }

        return false;
    }

    private bool TryResolveRetrievalControlledCargoPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid cargo,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;

        if (cargo == EntityUid.Invalid || TerminatingOrDeleted(cargo))
            return false;

        if (IsRetrievalCargoAlreadyAtPinpointerDestination(store, contract, state, cargo))
            return false;

        return TryResolveRetrievalCargoDestinationPinpointerTarget(store, contract, state, out target);
    }

    private bool TryResolveRetrievalCarriedCargoPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid cargo,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;

        if (cargo == EntityUid.Invalid || TerminatingOrDeleted(cargo))
            return false;

        if (IsRetrievalCargoAlreadyAtPinpointerDestination(store, contract, state, cargo))
            return false;

        if (!TryComp(cargo, out TransformComponent? xform) || !IsTargetInEntityContainer(xform))
            return false;

        return TryResolveRetrievalCargoDestinationPinpointerTarget(store, contract, state, out target);
    }

    private bool TryResolveRetrievalStoreReturnPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        if (contract.Config.RetrievalDestinationType != NcRetrievalDestinationTargetType.StoreUi)
            return false;

        var required = CalculateTotalRequired(GetEffectiveTargets(contract));
        if (required <= 0)
            return false;

        var hasOutstandingCargo = false;
        var deliveredCargo = 0;
        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var cargo = state.RetrievalSpawnedEntities[i];
            if (cargo == EntityUid.Invalid || TerminatingOrDeleted(cargo))
                continue;

            if (IsRetrievalCargoAlreadyAtPinpointerDestination(store, contract, state, cargo))
            {
                deliveredCargo++;
                continue;
            }

            hasOutstandingCargo = true;
            break;
        }

        if (hasOutstandingCargo || deliveredCargo < required)
            return false;

        target = store;
        return true;
    }

    private bool IsRetrievalCargoAlreadyAtPinpointerDestination(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid cargo
    )
    {
        if (cargo == EntityUid.Invalid || TerminatingOrDeleted(cargo))
            return false;

        var config = contract.Config;
        if (config.RetrievalDestinationType == NcRetrievalDestinationTargetType.StoreUi)
            return IsTrackedDeliveryTargetAtStore(store, cargo);

        return RequiresRetrievalRouteDelivery(contract) &&
            (state.RetrievalDeliveredEntities.Contains(cargo) ||
                IsRetrievalCargoDelivered(store, cargo, config, state));
    }

    private bool TryResolveRetrievalCargoDestinationPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        var config = contract.Config;
        switch (config.RetrievalDestinationType)
        {
            case NcRetrievalDestinationTargetType.StoreUi:
                target = store;
                return true;

            case NcRetrievalDestinationTargetType.MarkerGroup:
                if (state.DeliveryDropoffEntity is { } beacon &&
                    beacon != EntityUid.Invalid &&
                    !TerminatingOrDeleted(beacon))
                {
                    target = beacon;
                    return true;
                }

                return false;

            case NcRetrievalDestinationTargetType.ContainerGroup:
                return TryResolveRetrievalContainerDestinationPinpointerTarget(config, out target);

            default:
                return false;
        }
    }

    private bool TryResolveRetrievalContainerDestinationPinpointerTarget(
        ContractObjectiveConfigData config,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        if (string.IsNullOrWhiteSpace(config.RetrievalDestinationId))
            return false;

        CollectTurnInContainersByGroup(config.RetrievalDestinationId, _turnInContainerQueryScratch);
        for (var i = 0; i < _turnInContainerQueryScratch.Count; i++)
        {
            var container = _turnInContainerQueryScratch[i];
            target = container;
            _turnInContainerQueryScratch.Clear();
            return true;
        }

        _turnInContainerQueryScratch.Clear();
        return false;
    }

    private bool IsRetrievalCargoControlledByUser(EntityUid cargo, EntityUid user)
    {
        if (cargo == EntityUid.Invalid || user == EntityUid.Invalid || TerminatingOrDeleted(cargo))
            return false;

        if (TryComp(cargo, out PullableComponent? directPullable) && directPullable.Puller == user)
            return true;

        if (!TryGetContainedEntityRoot(cargo, out var root))
            return false;

        if (root == user)
            return true;

        return TryComp(root, out PullableComponent? rootPullable) && rootPullable.Puller == user;
    }

    private bool TryResolveRetrievalSpawnedParentChangePinpointerTarget(
        EntityUid cargo,
        out (EntityUid Store, string ContractId) key,
        out ObjectiveRuntimeState state,
        out EntityUid target,
        out EntityUid carrier
    )
    {
        key = default;
        state = default!;
        target = EntityUid.Invalid;
        carrier = EntityUid.Invalid;

        if (!_objectiveRuntime.ByRetrievalCargo.TryGetValue(cargo, out var candidateKey) ||
            !_objectiveRuntime.ByContract.TryGetValue(candidateKey, out var candidateState) ||
            !TryGetObjectiveContract(candidateKey, out _, out var contract) ||
            !contract.Taken ||
            contract.Runtime.Failed ||
            !UsesRetrievalSpawnedPinpointerTarget(contract))
            return false;

        RefreshPinpointerRuntimeState(candidateKey.Store, candidateKey.ContractId, contract);
        if (contract.Runtime.Failed || !_objectiveRuntime.ByContract.TryGetValue(candidateKey, out candidateState))
            return false;

        if (!TryResolveContractPinpointerTarget(
            candidateKey.Store,
            candidateKey.ContractId,
            contract,
            candidateState,
            out target))
            return false;

        if (TryGetContainedEntityRoot(cargo, out var cargoCarrier))
            carrier = cargoCarrier;

        key = candidateKey;
        state = candidateState;
        return true;
    }
}
