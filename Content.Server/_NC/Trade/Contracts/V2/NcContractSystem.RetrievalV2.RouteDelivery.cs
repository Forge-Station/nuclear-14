using Content.Shared._NC.Trade;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly List<EntityUid> _retrievalRouteContainerItemsScratch = new();

    private bool TryInitializeRetrievalRouteDeliveryRuntime(
        EntityUid store,
        string contractId,
        ContractServerData contract)
    {
        var config = contract.Config;
        if (!config.RetrievalProofEnabled)
            return true;

        if (!config.RetrievalSpawnEnabled || !config.RetrievalRequireSpawnedEntities)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval route init failed for '{contractId}': proof routes require spawned tracked cargo.");
            return false;
        }

        if (config.RetrievalDestinationType == NcRetrievalDestinationTargetType.StoreUi)
            return true;

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);

        if (config.RetrievalDestinationType == NcRetrievalDestinationTargetType.MarkerGroup)
        {
            if (config.RetrievalDestinationPoint == null)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Retrieval route init failed for '{contractId}': marker destination selector is missing.");
                return false;
            }

            if (!TryResolveObjectiveSpawnCoordinates(store, config.RetrievalDestinationPoint, out var destCoords, fallbackToStore: false))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Retrieval route init failed for '{contractId}': cannot resolve destination marker group '{config.RetrievalDestinationId}'.");
                return false;
            }

            state.RetrievalDeliveryCoordinates = destCoords;
        }

        if (!state.RetrievalRouteDeliveryActive)
        {
            state.RetrievalRouteDeliveryActive = true;
            _activeRetrievalRouteDeliveries++;
        }

        return true;
    }

    private void UpdateRetrievalRouteDeliveries()
    {
        if (_objectiveRuntimeByContract.Count == 0)
            return;

        _objectiveRuntimeKeysScratch.Clear();
        foreach (var (key, state) in _objectiveRuntimeByContract)
        {
            if (!state.RetrievalRouteDeliveryActive || state.RetrievalRouteDeliveryCompleted)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                !contract.IsInventoryDelivery ||
                !contract.Config.RetrievalProofEnabled ||
                contract.Completed && state.ProofSpawned)
            {
                continue;
            }

            _objectiveRuntimeKeysScratch.Add(key);
        }

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
            UpdateRetrievalRouteDelivery(_objectiveRuntimeKeysScratch[i]);

        _objectiveRuntimeKeysScratch.Clear();
    }

    private void UpdateRetrievalRouteDelivery((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            !TryGetObjectiveContract(key, out var comp, out var contract))
        {
            return;
        }

        if (!contract.Taken || !contract.IsInventoryDelivery || !contract.Config.RetrievalProofEnabled)
            return;

        PruneRetrievalSpawnedEntities(state);
        if (state.RetrievalSpawnedEntities.Count == 0)
        {
            ResetContractProgress(contract);
            return;
        }

        UpdateRetrievalDeliveredCargoSet(key.Store, contract, state);
        SetTrackedDeliveryProgress(contract, state.RetrievalDeliveredEntities.Count);

        if (!contract.Completed || state.ProofSpawned)
            return;

        if (!TryResolveRetrievalRouteProofCoordinates(contract, state, out var proofCoords))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval route '{key.ContractId}' completed but proof coordinates could not be resolved.");
            return;
        }

        if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, proofCoords))
            return;

        state.RetrievalRouteDeliveryCompleted = true;
        state.RetrievalRouteDeliveryActive = false;
        _activeRetrievalRouteDeliveries = Math.Max(0, _activeRetrievalRouteDeliveries - 1);

        if (contract.Config.RetrievalConsumeCargo)
            ConsumeDeliveredRetrievalCargo(state);

        if (contract.Config.RetrievalGuidancePinpointerEnabled)
            RetargetObjectivePinpointers(key, state, key.Store);
        else
            CleanupObjectivePinpointers(key, state);
    }

    private void UpdateRetrievalDeliveredCargoSet(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        var config = contract.Config;
        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            var cargo = state.RetrievalSpawnedEntities[i];
            if (cargo == EntityUid.Invalid || TerminatingOrDeleted(cargo))
                continue;

            if (state.RetrievalDeliveredEntities.Contains(cargo))
                continue;

            if (IsRetrievalCargoDelivered(store, cargo, config, state))
                state.RetrievalDeliveredEntities.Add(cargo);
        }
    }

    private bool IsRetrievalCargoDelivered(
        EntityUid store,
        EntityUid cargo,
        ContractObjectiveConfigData config,
        ObjectiveRuntimeState state)
    {
        return config.RetrievalDestinationType switch
        {
            NcRetrievalDestinationTargetType.MarkerGroup => IsRetrievalCargoAtMarkerDestination(cargo, config, state),
            NcRetrievalDestinationTargetType.ContainerGroup => IsRetrievalCargoInTurnInContainer(cargo, config),
            NcRetrievalDestinationTargetType.StoreUi => IsTrackedDeliveryTargetAtStore(store, cargo),
            _ => false
        };
    }

    private bool IsRetrievalCargoAtMarkerDestination(
        EntityUid cargo,
        ContractObjectiveConfigData config,
        ObjectiveRuntimeState state)
    {
        if (state.RetrievalDeliveryCoordinates is not { } destination)
            return false;

        if (!TryComp(cargo, out TransformComponent? cargoXform))
            return false;

        if (IsTargetInEntityContainer(cargoXform))
            return false;

        var cargoMap = _xform.ToMapCoordinates(cargoXform.Coordinates);
        var destinationMap = _xform.ToMapCoordinates(destination);
        if (cargoMap.MapId != destinationMap.MapId)
            return false;

        var cargoPos = _xform.GetWorldPosition(cargoXform);
        var delta = cargoPos - destinationMap.Position;
        var radius = Math.Max(0.25f, config.RetrievalDestinationRadius);
        return delta.LengthSquared() <= radius * radius;
    }

    private bool IsRetrievalCargoInTurnInContainer(
        EntityUid cargo,
        ContractObjectiveConfigData config)
    {
        var query = EntityQueryEnumerator<NcContractTurnInContainerComponent>();
        while (query.MoveNext(out var container, out var turnIn))
        {
            if (!turnIn.Groups.Contains(config.RetrievalDestinationId))
                continue;

            _retrievalRouteContainerItemsScratch.Clear();
            _logic.ScanInventoryItems(container, _retrievalRouteContainerItemsScratch);

            for (var i = 0; i < _retrievalRouteContainerItemsScratch.Count; i++)
            {
                if (_retrievalRouteContainerItemsScratch[i] == cargo)
                {
                    _retrievalRouteContainerItemsScratch.Clear();
                    return true;
                }
            }
        }

        _retrievalRouteContainerItemsScratch.Clear();
        return false;
    }

    private bool TryResolveRetrievalRouteProofCoordinates(
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityCoordinates coords)
    {
        coords = EntityCoordinates.Invalid;

        if (contract.Config.RetrievalDestinationType == NcRetrievalDestinationTargetType.ContainerGroup)
        {
            var query = EntityQueryEnumerator<NcContractTurnInContainerComponent>();
            while (query.MoveNext(out var container, out var turnIn))
            {
                if (!turnIn.Groups.Contains(contract.Config.RetrievalDestinationId))
                    continue;

                if (TryComp(container, out TransformComponent? containerXform))
                {
                    coords = containerXform.Coordinates;
                    return true;
                }
            }
        }

        if (state.RetrievalDeliveryCoordinates is { } destinationCoords)
        {
            coords = destinationCoords;
            return true;
        }

        foreach (var ent in state.RetrievalDeliveredEntities)
        {
            if (TryComp(ent, out TransformComponent? xform))
            {
                coords = xform.Coordinates;
                return true;
            }
        }

        return false;
    }

    private void ConsumeDeliveredRetrievalCargo(ObjectiveRuntimeState state)
    {
        foreach (var cargo in state.RetrievalDeliveredEntities)
        {
            state.RetrievalSpawnedEntities.Remove(cargo);
            if (cargo != EntityUid.Invalid && !TerminatingOrDeleted(cargo))
                Del(cargo);
        }

        state.RetrievalDeliveredEntities.Clear();
    }
}
