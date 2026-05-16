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
        if (UsesRetrievalRouteReturnPinpointerTarget(contract, state))
        {
            pinpointerTarget = store;
        }
        else if (UsesRetrievalSpawnedPinpointerTarget(contract))
        {
            if (!TryResolveRetrievalSpawnedPinpointerTarget(contract, state, out pinpointerTarget))
                return false;
        }
        else if (contract.Completed)
        {
            if (state.ProofEntity is not { } proof || proof == EntityUid.Invalid || TerminatingOrDeleted(proof))
                return false;

            pinpointerTarget = proof;
        }
        else
        {
            if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && !state.GhostRoleTaken)
                return false;

            if (state.TargetEntity is not { } target || target == EntityUid.Invalid || TerminatingOrDeleted(target))
                return false;

            pinpointerTarget = ResolveObjectivePinpointerTarget(contract, state, target);
            if (pinpointerTarget == EntityUid.Invalid || TerminatingOrDeleted(pinpointerTarget))
                return false;
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

    private static bool UsesRetrievalSpawnedPinpointerTarget(ContractServerData contract)
    {
        var config = contract.Config;
        return contract.IsInventoryDelivery &&
               config.RetrievalSpawnEnabled &&
               config.RetrievalRequireSpawnedEntities;
    }

    private bool TryResolveRetrievalSpawnedPinpointerTarget(
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!UsesRetrievalSpawnedPinpointerTarget(contract))
            return false;

        PruneRetrievalSpawnedEntities(state);
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
}
