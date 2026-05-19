using Content.Shared._NC.Trade;
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
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
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
        return contract.IsInventoryDelivery &&
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

    private static bool UsesRetrievalSpawnedPinpointerTarget(ContractServerData contract)
    {
        var config = contract.Config;
        return contract.IsInventoryDelivery &&
               config.RetrievalSpawnEnabled &&
               config.RetrievalRequireSpawnedEntities;
    }

    private bool TryResolveRetrievalSpawnedPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
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
        out EntityUid target)
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

        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var candidate = state.RetrievalSpawnedEntities[i];
            if (!TryGetContainedEntityRoot(candidate, out var cargoCarrier) || cargoCarrier != user)
                continue;

            if (!TryResolveRetrievalCarriedCargoPinpointerTarget(store, contract, state, candidate, out target))
                continue;

            return true;
        }

        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var candidate = state.RetrievalSpawnedEntities[i];
            if (candidate == EntityUid.Invalid || TerminatingOrDeleted(candidate))
                continue;

            target = candidate;
            return true;
        }

        return false;
    }

    private bool TryResolveRetrievalCarriedCargoPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid cargo,
        out EntityUid target)
    {
        target = EntityUid.Invalid;

        if (cargo == EntityUid.Invalid || TerminatingOrDeleted(cargo))
            return false;

        if (!TryComp(cargo, out TransformComponent? xform) || !IsTargetInEntityContainer(xform))
            return false;

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
        out EntityUid target)
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

    private bool TryResolveRetrievalSpawnedParentChangePinpointerTarget(
        EntityUid cargo,
        out (EntityUid Store, string ContractId) key,
        out ObjectiveRuntimeState state,
        out EntityUid target,
        out EntityUid carrier)
    {
        key = default;
        state = default!;
        target = EntityUid.Invalid;
        carrier = EntityUid.Invalid;

        if (!_objectiveRuntimeByRetrievalCargo.TryGetValue(cargo, out var candidateKey) ||
            !_objectiveRuntimeByContract.TryGetValue(candidateKey, out var candidateState) ||
            !TryGetObjectiveContract(candidateKey, out _, out var contract) ||
            !contract.Taken ||
            contract.Runtime.Failed ||
            !UsesRetrievalSpawnedPinpointerTarget(contract) ||
            !TryResolveRetrievalSpawnedPinpointerTarget(candidateKey.Store, contract, candidateState, out target))
        {
            return false;
        }

        if (TryGetContainedEntityRoot(cargo, out var cargoCarrier))
            carrier = cargoCarrier;

        key = candidateKey;
        state = candidateState;
        return true;
    }

    private bool TrySpawnObjectivePinpointer(
        EntityUid user,
        EntityUid target,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config,
        EntityCoordinates spawnCoords
    )
    {
        if (!CanIssueContractPinpointer(key, state, config))
        {
            var limit = GetContractPinpointerLimit(config);
            Sawmill.Info(
                $"[Contracts] Objective init blocked for '{key.ContractId}': contract pinpointer limit reached ({limit}).");
            return false;
        }

        if (!TryResolveObjectivePinpointerPrototype(config, out var pinpointerProtoId))
            return false;

        var pinpointerCoords = ResolveObjectivePinpointerSpawnCoordinates(user, spawnCoords);
        if (!TrySpawnObjectivePinpointerEntity(key, pinpointerProtoId, pinpointerCoords, out var pinpointer))
            return false;

        RegisterObjectivePinpointer(user, target, key, state, pinpointer);
        return true;
    }

    private bool TryResolveObjectivePinpointerPrototype(
        ContractObjectiveConfigData config,
        out string pinpointerProtoId)
    {
        pinpointerProtoId = ResolvePinpointerPrototypeId(config.PinpointerPrototype);
        if (_prototypes.HasIndex<EntityPrototype>(pinpointerProtoId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Objective init: pinpointer proto '{pinpointerProtoId}' not found, fallback to {NcContractTuning.DefaultContractPinpointerPrototypeId}.");
        pinpointerProtoId = NcContractTuning.DefaultContractPinpointerPrototypeId;
        return _prototypes.HasIndex<EntityPrototype>(pinpointerProtoId);
    }

    private EntityCoordinates ResolveObjectivePinpointerSpawnCoordinates(EntityUid user, EntityCoordinates fallbackCoords)
    {
        if (TryComp(user, out TransformComponent? userXform))
            return userXform.Coordinates;

        return fallbackCoords;
    }

    private bool TrySpawnObjectivePinpointerEntity(
        (EntityUid Store, string ContractId) key,
        string pinpointerProtoId,
        EntityCoordinates pinpointerCoords,
        out EntityUid pinpointer)
    {
        try
        {
            pinpointer = Spawn(pinpointerProtoId, pinpointerCoords);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn pinpointer '{pinpointerProtoId}': {e}");
            pinpointer = EntityUid.Invalid;
            return false;
        }
    }

    private void RegisterObjectivePinpointer(
        EntityUid user,
        EntityUid target,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid pinpointer)
    {
        _pinpointer.SetTarget(pinpointer, target);
        _pinpointer.SetActive(pinpointer, true);
        state.PinpointerEntities.Add(pinpointer);
        _objectiveRuntimeByPinpointer[pinpointer] = key;
        _objectiveRuntimePinpointerOwners[pinpointer] = user;
        _logic.QueuePickupToHandsOrCrateNextTick(user, pinpointer);
    }

    private void RetargetObjectivePinpointers(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid target)
    {
        if (target == EntityUid.Invalid || TerminatingOrDeleted(target))
            return;

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }
    }

    private void RetargetObjectivePinpointersForOwner(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid owner,
        EntityUid target)
    {
        if (owner == EntityUid.Invalid ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(owner) ||
            TerminatingOrDeleted(target))
        {
            return;
        }

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            if (!_objectiveRuntimePinpointerOwners.TryGetValue(pinpointer, out var pinpointerOwner) ||
                pinpointerOwner != owner)
            {
                continue;
            }

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }
    }

    private bool CanIssueContractPinpointer(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config)
    {
        PruneInvalidPinpointers(key, state);
        return state.PinpointerEntities.Count < GetContractPinpointerLimit(config);
    }

    private static int GetContractPinpointerLimit(ContractObjectiveConfigData config)
    {
        if (config.RetrievalGuidancePinpointerEnabled && config.RetrievalGuidanceMaxActivePinpointers > 0)
            return config.RetrievalGuidanceMaxActivePinpointers;

        return NcContractTuning.MaxActiveContractPinpointers;
    }

    private void PruneInvalidPinpointers((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _objectivePinpointersScratch.Clear();
        foreach (var pinpointer in state.PinpointerEntities)
            if (TerminatingOrDeleted(pinpointer))
                _objectivePinpointersScratch.Add(pinpointer);

        for (var i = 0; i < _objectivePinpointersScratch.Count; i++)
            UnregisterIssuedPinpointer(_objectivePinpointersScratch[i], key);

        _objectivePinpointersScratch.Clear();
    }

    private void UnregisterIssuedPinpointer(EntityUid pinpointer, (EntityUid Store, string ContractId) key)
    {
        _objectiveRuntimeByPinpointer.Remove(pinpointer);
        _objectiveRuntimePinpointerOwners.Remove(pinpointer);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            state.PinpointerEntities.Remove(pinpointer);
    }

    private void CleanupObjectivePinpointers(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _objectivePinpointersScratch.Clear();
        _objectivePinpointersScratch.AddRange(state.PinpointerEntities);

        for (var i = 0; i < _objectivePinpointersScratch.Count; i++)
        {
            var pinpointer = _objectivePinpointersScratch[i];
            UnregisterIssuedPinpointer(pinpointer, key);

            if (!TerminatingOrDeleted(pinpointer))
                Del(pinpointer);
        }

        state.PinpointerEntities.Clear();
        _objectivePinpointersScratch.Clear();
    }

    private bool TryGetContainedEntityRoot(EntityUid entity, out EntityUid root)
    {
        root = EntityUid.Invalid;
        if (!TryComp(entity, out TransformComponent? xform) || !IsTargetInEntityContainer(xform))
            return false;

        var current = xform.ParentUid;
        for (var guard = 0; guard < 32; guard++)
        {
            if (current == EntityUid.Invalid)
                break;

            root = current;

            if (!TryComp(current, out TransformComponent? parentXform))
                break;

            var parent = parentXform.ParentUid;
            if (parent == EntityUid.Invalid)
                break;

            if (parentXform.MapUid is { } mapUid && parent == mapUid)
                break;

            if (parentXform.GridUid is { } gridUid && parent == gridUid)
                break;

            current = parent;
        }

        return root != EntityUid.Invalid;
    }
}
