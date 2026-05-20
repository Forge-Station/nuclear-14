using Content.Shared._NC.Trade;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
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
