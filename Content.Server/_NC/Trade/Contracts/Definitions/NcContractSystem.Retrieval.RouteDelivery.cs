using Content.Shared._NC.Trade;


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

    private static bool RequiresRetrievalDestinationProofClaim(ContractServerData contract) =>
        RequiresRetrievalRouteDelivery(contract) &&
        contract.Config.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof;

    private bool TryInitializeRetrievalRouteDeliveryRuntime(
        EntityUid store,
        string contractId,
        ContractServerData contract
    )
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

            if (!TryResolveObjectiveSpawnCoordinates(
                store,
                config.RetrievalDestinationPoint,
                out var destCoords,
                false))
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
            return false;

        if (!state.RetrievalRouteDeliveryActive)
        {
            state.RetrievalRouteDeliveryActive = true;
            _objectiveRuntime.ActiveRetrievalRouteDeliveries.Add((store, contractId));
        }

        return true;
    }

    private bool TryValidateRetrievalRouteContainerDestination(
        string contractId,
        ContractObjectiveConfigData config
    )
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
}
