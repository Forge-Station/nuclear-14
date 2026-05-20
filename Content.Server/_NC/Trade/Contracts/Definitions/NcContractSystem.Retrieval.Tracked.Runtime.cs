using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void RegisterRetrievalSpawnedCargo(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid cargo)
    {
        if (state.RetrievalSpawnedEntitySet.Add(cargo))
            state.RetrievalSpawnedEntities.Add(cargo);

        _objectiveRuntime.ByRetrievalCargo[cargo] = key;
    }

    private void UnregisterRetrievalSpawnedCargo(EntityUid cargo)
    {
        if (cargo == EntityUid.Invalid)
            return;

        _objectiveRuntime.ByRetrievalCargo.Remove(cargo);
    }

    private void UnregisterRetrievalSpawnedCargoTakePlan(
        ContractServerData contract,
        List<ClaimTakeEntry> takePlan,
        ClaimTakeJournal? journal = null)
    {
        if (!RequiresRetrievalSpawnedTurnIn(contract))
            return;

        for (var i = 0; i < takePlan.Count; i++)
        {
            var cargo = takePlan[i].Entity;
            if (journal != null && _objectiveRuntime.ByRetrievalCargo.TryGetValue(cargo, out var key))
                journal.TrackRetrievalCargo(cargo, key);

            UnregisterRetrievalSpawnedCargo(cargo);
        }
    }

    private void RemoveRetrievalSpawnedCargoFromState(ObjectiveRuntimeState state, EntityUid cargo)
    {
        state.RetrievalSpawnedEntities.Remove(cargo);
        state.RetrievalSpawnedEntitySet.Remove(cargo);
        state.RetrievalDeliveredEntities.Remove(cargo);
    }

    private void OnRetrievalSpawnedCargoDestroyed(
        (EntityUid Store, string ContractId) key,
        EntityUid cargo)
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        RemoveRetrievalSpawnedCargoFromState(state, cargo);

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken || contract.Runtime.Failed)
            return;

        if (!RequiresRetrievalSpawnedTurnIn(contract) &&
            !RequiresRetrievalRouteDelivery(contract))
        {
            return;
        }

        if (state.ProofSpawned || state.RetrievalRouteDeliveryCompleted)
            return;

        Sawmill.Warning(
            $"[Contracts] Retrieval cargo for '{key.ContractId}' was destroyed before turn-in on {ToPrettyString(key.Store)}; contract failed.");

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);
    }

    private bool TryFailRetrievalSpawnedTurnInIfTrackedCargoWasLost(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!RequiresRetrievalSpawnedTurnIn(contract) ||
            !contract.Taken ||
            contract.Runtime.Failed)
        {
            return false;
        }

        var required = CalculateTotalRequired(GetEffectiveTargets(contract));
        if (required <= 0)
            return false;

        var accepted = CalculateAppliedTurnedInProgress(contract, state);
        if (accepted >= required)
            return false;

        var stillPossible = accepted + state.RetrievalSpawnedEntities.Count;
        if (stillPossible >= required)
            return false;

        if (!TryGetObjectiveContract(key, out var comp, out _))
            return false;

        Sawmill.Warning(
            $"[Contracts] Retrieval cargo for '{key.ContractId}' is no longer available " +
            $"({accepted}/{required} accepted, {state.RetrievalSpawnedEntities.Count} remaining). Contract failed.");

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);
        return true;
    }

    private void PruneRetrievalSpawnedEntities(ObjectiveRuntimeState state)
    {
        for (var i = state.RetrievalSpawnedEntities.Count - 1; i >= 0; i--)
        {
            var ent = state.RetrievalSpawnedEntities[i];
            if (ent == EntityUid.Invalid || TerminatingOrDeleted(ent))
            {
                UnregisterRetrievalSpawnedCargo(ent);
                state.RetrievalSpawnedEntities.RemoveAt(i);
                state.RetrievalSpawnedEntitySet.Remove(ent);
            }
        }
    }

    private List<EntityUid> FilterRetrievalSpawnedSourceItems(
        IReadOnlyList<EntityUid>? sourceItems,
        ObjectiveRuntimeState state)
    {
        var filtered = new List<EntityUid>();
        if (sourceItems == null || sourceItems.Count == 0 || state.RetrievalSpawnedEntities.Count == 0)
            return filtered;

        for (var i = 0; i < sourceItems.Count; i++)
        {
            var ent = sourceItems[i];
            if (ent == EntityUid.Invalid)
                continue;

            if (IsRetrievalSpawnedEntity(ent, state))
                filtered.Add(ent);
        }

        return filtered;
    }

    private static bool IsRetrievalSpawnedEntity(EntityUid ent, ObjectiveRuntimeState state)
    {
        return state.RetrievalSpawnedEntitySet.Contains(ent);
    }
}
