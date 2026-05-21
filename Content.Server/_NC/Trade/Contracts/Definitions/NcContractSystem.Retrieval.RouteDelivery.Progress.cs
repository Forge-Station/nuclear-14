using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
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
                continue;

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
            return;

        UpdateRetrievalRouteDelivery(key);
    }

    private bool TryUpdateRetrievalRouteDeliveryProgress(
        EntityUid store,
        string contractId,
        ContractServerData contract
    )
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
            return;

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

    private static int GetRetrievalRouteDeliveryProgress(ObjectiveRuntimeState state) =>
        Math.Max(0, state.RetrievalAcceptedCargoCount) + state.RetrievalDeliveredEntities.Count;

    private bool TryFailRetrievalRouteIfTrackedCargoWasLost(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        var config = contract.Config;
        if (!RequiresRetrievalRouteDelivery(contract) ||
            !config.RetrievalSpawnEnabled ||
            !config.RetrievalRequireSpawnedEntities ||
            state.ProofSpawned ||
            state.RetrievalRouteDeliveryCompleted)
            return false;

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
        ObjectiveRuntimeState state
    )
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
        ObjectiveRuntimeState state
    ) =>
        config.RetrievalDestinationType switch
        {
            NcRetrievalDestinationTargetType.MarkerGroup => IsRetrievalCargoAtMarkerDestination(cargo, config, state),
            NcRetrievalDestinationTargetType.ContainerGroup => IsRetrievalCargoInTurnInContainer(cargo, config),
            NcRetrievalDestinationTargetType.StoreUi => IsTrackedDeliveryTargetAtStore(store, cargo),
            _ => false
        };

    private bool IsRetrievalCargoAtMarkerDestination(
        EntityUid cargo,
        ContractObjectiveConfigData config,
        ObjectiveRuntimeState state
    )
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
        ContractObjectiveConfigData config
    ) =>
        TryResolveRetrievalContainerCargoCoordinates(cargo, config, out _);
}
