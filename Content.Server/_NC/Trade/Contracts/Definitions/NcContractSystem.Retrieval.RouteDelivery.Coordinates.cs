using Content.Shared._NC.Trade;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
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
}
