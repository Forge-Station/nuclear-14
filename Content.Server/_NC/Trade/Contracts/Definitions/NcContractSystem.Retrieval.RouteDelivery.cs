using Content.Shared._NC.Trade;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly List<EntityUid> _retrievalRouteContainerItemsScratch = new();
    private readonly List<EntityUid> _retrievalRouteDeliveredPruneScratch = new();

    private static bool RequiresRetrievalRouteDelivery(ContractServerData contract)
    {
        var config = contract.Config;
        return contract.IsRetrievalRouteDelivery &&
               IsTrackedRetrievalRouteDeliveryConfig(config);
    }

    private static bool RequiresRetrievalDestinationProofClaim(ContractServerData contract)
    {
        return RequiresRetrievalRouteDelivery(contract) &&
               contract.Config.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof;
    }

    private bool TryInitializeRetrievalRouteDeliveryRuntime(
        EntityUid store,
        string contractId,
        ContractServerData contract)
    {
        var config = contract.Config;
        if (!RequiresRetrievalRouteDelivery(contract))
            return true;

        if (!config.RetrievalSpawnEnabled || !config.RetrievalRequireSpawnedEntities)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval route init failed for '{contractId}': route delivery requires spawned tracked cargo.");
            return false;
        }

        if (config.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof && !config.RetrievalProofEnabled)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval route init failed for '{contractId}': DestinationProof route has no proof configured.");
            return false;
        }

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);

        if (config.RetrievalDestinationType == NcRetrievalDestinationTargetType.MarkerGroup)
        {
            if (config.RetrievalDestinationPoint == null)
            {
                Sawmill.Warning(
                    $"[Contracts] Retrieval route init failed for '{contractId}': marker destination selector is missing.");
                return false;
            }

            if (!TryResolveObjectiveSpawnCoordinates(store, config.RetrievalDestinationPoint, out var destCoords, fallbackToStore: false))
            {
                Sawmill.Warning(
                    $"[Contracts] Retrieval route init failed for '{contractId}': cannot resolve destination marker group '{config.RetrievalDestinationId}'.");
                return false;
            }

            state.RetrievalDeliveryCoordinates = destCoords;
            if (!TrySpawnDeliveryDropoffMarker(contractId, state, destCoords))
                return false;
        }
        else if (config.RetrievalDestinationType == NcRetrievalDestinationTargetType.ContainerGroup &&
                 !TryValidateRetrievalRouteContainerDestination(contractId, config))
        {
            return false;
        }

        if (!state.RetrievalRouteDeliveryActive)
        {
            state.RetrievalRouteDeliveryActive = true;
            _objectiveRuntime.ActiveRetrievalRouteDeliveries.Add((store, contractId));
        }

        return true;
    }

    private bool TryValidateRetrievalRouteContainerDestination(
        string contractId,
        ContractObjectiveConfigData config)
    {
        if (string.IsNullOrWhiteSpace(config.RetrievalDestinationId))
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval route init failed for '{contractId}': container destination group is missing.");
            return false;
        }

        CollectTurnInContainersByGroup(config.RetrievalDestinationId, _turnInContainerQueryScratch);
        var found = _turnInContainerQueryScratch.Count > 0;
        _turnInContainerQueryScratch.Clear();

        if (found)
            return true;

        Sawmill.Warning(
            $"[Contracts] Retrieval route init failed for '{contractId}': no turn-in container found for destination group '{config.RetrievalDestinationId}'.");
        return false;
    }

    private void UpdateRetrievalRouteDeliveries()
    {
        if (_objectiveRuntime.ActiveRetrievalRouteDeliveries.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ActiveRetrievalRouteDeliveries)
            _objectiveRuntime.KeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
        {
            var key = _objectiveRuntime.KeysScratch[i];
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            {
                _objectiveRuntime.ActiveRetrievalRouteDeliveries.Remove(key);
                continue;
            }

            if (!state.RetrievalRouteDeliveryActive || state.RetrievalRouteDeliveryCompleted)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                !RequiresRetrievalRouteDelivery(contract) ||
                contract.Completed && state.RetrievalRouteDeliveryCompleted)
            {
                continue;
            }

            UpdateRetrievalRouteDelivery(key);
        }

        _objectiveRuntime.KeysScratch.Clear();
    }

    private void RefreshRetrievalRouteDeliveryForClaim(EntityUid store, string contractId, ContractServerData contract)
    {
        if (!RequiresRetrievalRouteDelivery(contract))
            return;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.RetrievalRouteDeliveryCompleted ||
            !state.RetrievalRouteDeliveryActive)
        {
            return;
        }

        UpdateRetrievalRouteDelivery(key);
    }

    private bool TryUpdateRetrievalRouteDeliveryProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        if (!RequiresRetrievalRouteDelivery(contract))
            return false;

        RefreshRetrievalRouteDeliveryForClaim(store, contractId, contract);
        SyncContractFlowStatus(contract);
        return true;
    }

    private void UpdateRetrievalRouteDelivery((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            !TryGetObjectiveContract(key, out var comp, out var contract))
        {
            return;
        }

        if (!contract.Taken || !RequiresRetrievalRouteDelivery(contract))
            return;

        PruneRetrievalSpawnedEntities(state);
        PruneRetrievalDeliveredEntities(state);
        if (TryFailRetrievalRouteIfTrackedCargoWasLost(key, comp, contract, state))
            return;

        UpdateRetrievalDeliveredCargoProgress(key.Store, contract, state);
        SetTrackedDeliveryProgress(contract, GetRetrievalRouteDeliveryProgress(state));

        if (!contract.Completed)
        {
            RetargetRetrievalPinpointersToCurrentStep(key, contract, state);
            return;
        }

        if (RequiresRetrievalDestinationProofClaim(contract) && !state.ProofSpawned)
        {
            if (!TryResolveRetrievalRouteProofCoordinates(contract, state, out var proofCoords))
            {
                Sawmill.Warning(
                    $"[Contracts] Retrieval route '{key.ContractId}' completed but proof coordinates could not be resolved.");
                return;
            }

            if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, proofCoords))
                return;
        }

        state.RetrievalRouteDeliveryCompleted = true;
        state.RetrievalRouteDeliveryActive = false;
        _objectiveRuntime.ActiveRetrievalRouteDeliveries.Remove(key);
        DeactivateTrackedDeliveryDropoff(key, state);

        if (contract.Config.RetrievalConsumeCargo)
            ConsumeDeliveredRetrievalCargo(state);

        if (contract.Config.RetrievalGuidancePinpointerEnabled)
        {
            if (TryResolveRetrievalRouteReturnPinpointerTarget(key.Store, contract, state, out var pinpointerTarget))
                RetargetObjectivePinpointers(key, state, pinpointerTarget);
            else
                RetargetObjectivePinpointers(key, state, key.Store);
        }
        else
            CleanupObjectivePinpointers(key, state);
    }

    private static int GetRetrievalRouteDeliveryProgress(ObjectiveRuntimeState state)
    {
        return Math.Max(0, state.RetrievalAcceptedCargoCount) + state.RetrievalDeliveredEntities.Count;
    }

    private bool TryFailRetrievalRouteIfTrackedCargoWasLost(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        var config = contract.Config;
        if (!RequiresRetrievalRouteDelivery(contract) ||
            !config.RetrievalSpawnEnabled ||
            !config.RetrievalRequireSpawnedEntities ||
            state.ProofSpawned ||
            state.RetrievalRouteDeliveryCompleted)
        {
            return false;
        }

        var required = GetTrackedDeliveryCompletionAmount(contract);
        if (required <= 0)
            return false;

        var accepted = GetRetrievalRouteDeliveryProgress(state);
        if (accepted >= required)
            return false;

        var stillPossible = accepted + state.RetrievalSpawnedEntities.Count;
        if (stillPossible >= required)
            return false;

        Sawmill.Warning(
            $"[Contracts] Retrieval route '{key.ContractId}' lost required tracked cargo before route delivery completed " +
            $"({accepted}/{required} delivered, {state.RetrievalSpawnedEntities.Count} remaining). Contract failed.");

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);
        return true;
    }

    private void UpdateRetrievalDeliveredCargoProgress(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        var config = contract.Config;
        for (var i = state.RetrievalSpawnedEntities.Count - 1; i >= 0; i--)
        {
            var cargo = state.RetrievalSpawnedEntities[i];
            if (cargo == EntityUid.Invalid || TerminatingOrDeleted(cargo))
                continue;

            if (state.RetrievalDeliveredEntities.Contains(cargo))
                continue;

            if (!IsRetrievalCargoDelivered(store, cargo, config, state))
                continue;

            if (TryResolveRetrievalDeliveredCargoCoordinates(cargo, config, state, out var acceptedCoords))
                state.RetrievalLastAcceptedCargoCoordinates = acceptedCoords;

            if (config.RetrievalConsumeCargo)
            {
                state.RetrievalAcceptedCargoCount++;
                state.RetrievalSpawnedEntities.RemoveAt(i);
                state.RetrievalSpawnedEntitySet.Remove(cargo);
                UnregisterRetrievalSpawnedCargo(cargo);

                if (!TerminatingOrDeleted(cargo))
                    Del(cargo);

                continue;
            }

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
        return TryResolveRetrievalContainerCargoCoordinates(cargo, config, out _);
    }

    private bool TryResolveRetrievalDeliveredCargoCoordinates(
        EntityUid cargo,
        ContractObjectiveConfigData config,
        ObjectiveRuntimeState state,
        out EntityCoordinates coords)
    {
        coords = EntityCoordinates.Invalid;

        if (config.RetrievalDestinationType == NcRetrievalDestinationTargetType.ContainerGroup &&
            TryResolveRetrievalContainerCargoCoordinates(cargo, config, out coords))
        {
            return true;
        }

        if (config.RetrievalDestinationType == NcRetrievalDestinationTargetType.MarkerGroup &&
            state.RetrievalDeliveryCoordinates is { } destinationCoords)
        {
            coords = destinationCoords;
            return true;
        }

        if (TryComp(cargo, out TransformComponent? cargoXform))
        {
            coords = cargoXform.Coordinates;
            return true;
        }

        return false;
    }

    private bool TryResolveRetrievalContainerCargoCoordinates(
        EntityUid cargo,
        ContractObjectiveConfigData config,
        out EntityCoordinates coords)
    {
        coords = EntityCoordinates.Invalid;

        CollectTurnInContainersByGroup(config.RetrievalDestinationId, _turnInContainerQueryScratch);
        for (var containerIndex = 0; containerIndex < _turnInContainerQueryScratch.Count; containerIndex++)
        {
            var container = _turnInContainerQueryScratch[containerIndex];

            _retrievalRouteContainerItemsScratch.Clear();
            _logic.ScanInventoryItems(container, _retrievalRouteContainerItemsScratch);

            for (var i = 0; i < _retrievalRouteContainerItemsScratch.Count; i++)
            {
                if (_retrievalRouteContainerItemsScratch[i] != cargo)
                    continue;

                if (TryComp(container, out TransformComponent? containerXform))
                {
                    coords = containerXform.Coordinates;
                    _retrievalRouteContainerItemsScratch.Clear();
                    _turnInContainerQueryScratch.Clear();
                    return true;
                }

                if (TryComp(cargo, out TransformComponent? cargoXform))
                {
                    coords = cargoXform.Coordinates;
                    _retrievalRouteContainerItemsScratch.Clear();
                    _turnInContainerQueryScratch.Clear();
                    return true;
                }

                _retrievalRouteContainerItemsScratch.Clear();
                _turnInContainerQueryScratch.Clear();
                return true;
            }
        }

        _retrievalRouteContainerItemsScratch.Clear();
        _turnInContainerQueryScratch.Clear();
        return false;
    }

    private bool TryResolveRetrievalRouteProofCoordinates(
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityCoordinates coords)
    {
        coords = EntityCoordinates.Invalid;

        if (contract.Config.RetrievalDestinationType == NcRetrievalDestinationTargetType.ContainerGroup &&
            TryResolveRetrievalContainerProofCoordinates(contract.Config, state, out coords))
        {
            return true;
        }

        if (state.RetrievalLastAcceptedCargoCoordinates is { } lastAcceptedCoords)
        {
            coords = lastAcceptedCoords;
            return true;
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

    private bool TryResolveRetrievalContainerProofCoordinates(
        ContractObjectiveConfigData config,
        ObjectiveRuntimeState state,
        out EntityCoordinates coords)
    {
        coords = EntityCoordinates.Invalid;

        if (state.RetrievalDeliveredEntities.Count == 0)
            return false;

        CollectTurnInContainersByGroup(config.RetrievalDestinationId, _turnInContainerQueryScratch);
        for (var containerIndex = 0; containerIndex < _turnInContainerQueryScratch.Count; containerIndex++)
        {
            var container = _turnInContainerQueryScratch[containerIndex];

            _retrievalRouteContainerItemsScratch.Clear();
            _logic.ScanInventoryItems(container, _retrievalRouteContainerItemsScratch);

            for (var i = 0; i < _retrievalRouteContainerItemsScratch.Count; i++)
            {
                var item = _retrievalRouteContainerItemsScratch[i];
                if (!state.RetrievalDeliveredEntities.Contains(item))
                    continue;

                if (TryComp(container, out TransformComponent? containerXform))
                {
                    coords = containerXform.Coordinates;
                    _retrievalRouteContainerItemsScratch.Clear();
                    _turnInContainerQueryScratch.Clear();
                    return true;
                }

                if (TryComp(item, out TransformComponent? itemXform))
                {
                    coords = itemXform.Coordinates;
                    _retrievalRouteContainerItemsScratch.Clear();
                    _turnInContainerQueryScratch.Clear();
                    return true;
                }
            }

            _retrievalRouteContainerItemsScratch.Clear();
        }

        _turnInContainerQueryScratch.Clear();
        return false;
    }

    private void PruneRetrievalDeliveredEntities(ObjectiveRuntimeState state)
    {
        if (state.RetrievalDeliveredEntities.Count == 0)
            return;

        _retrievalRouteDeliveredPruneScratch.Clear();
        foreach (var delivered in state.RetrievalDeliveredEntities)
        {
            if (delivered == EntityUid.Invalid || TerminatingOrDeleted(delivered))
                _retrievalRouteDeliveredPruneScratch.Add(delivered);
        }

        for (var i = 0; i < _retrievalRouteDeliveredPruneScratch.Count; i++)
            state.RetrievalDeliveredEntities.Remove(_retrievalRouteDeliveredPruneScratch[i]);

        _retrievalRouteDeliveredPruneScratch.Clear();
    }

    private void ConsumeDeliveredRetrievalCargo(ObjectiveRuntimeState state)
    {
        foreach (var cargo in state.RetrievalDeliveredEntities)
        {
            state.RetrievalSpawnedEntities.Remove(cargo);
            state.RetrievalSpawnedEntitySet.Remove(cargo);
            UnregisterRetrievalSpawnedCargo(cargo);
            if (cargo != EntityUid.Invalid && !TerminatingOrDeleted(cargo))
                Del(cargo);
        }

        state.RetrievalDeliveredEntities.Clear();
    }
}
