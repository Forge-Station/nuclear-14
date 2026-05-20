using Content.Shared._NC.Trade;
using Content.Shared.Mind;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void CleanupObjectiveRuntime(
        EntityUid store,
        string contractId,
        bool deleteTrackedEntities,
        bool deleteGuards = true
    )
    {
        var key = (store, contractId);

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is { } target)
        {
            _objectiveRuntime.ByTarget.Remove(target);
            state.TargetEntity = null;

            if (deleteTrackedEntities && !TerminatingOrDeleted(target))
                Del(target);
        }

        DeactivateTrackedDeliveryDropoff(key, state);

        CleanupRetrievalSpawnedEntities(state, deleteTrackedEntities);
        CleanupSpawnedHuntBodyTarget(state, deleteTrackedEntities);
        CleanupHuntSpawnedTargets(state, deleteTrackedEntities);

        CleanupObjectivePinpointers(key, state);
        CleanupGhostRoleSurvivalObjective(state);

        if (state.GuardEntities.Count > 0)
        {
            for (var i = 0; i < state.GuardEntities.Count; i++)
            {
                var guard = state.GuardEntities[i];
                _objectiveRuntime.ByGuard.Remove(guard);

                if (deleteGuards && !TerminatingOrDeleted(guard))
                    Del(guard);
            }

            state.GuardEntities.Clear();
        }

        if (state.ProofEntity is { } proof)
        {
            _objectiveRuntime.ByProof.Remove(proof);

            if (!TerminatingOrDeleted(proof))
                Del(proof);
        }

        state.ProofEntity = null;
        state.ProofSpawned = false;
        state.ProofToken = string.Empty;

        if (state.HuntActive)
        {
            state.HuntActive = false;
            _objectiveRuntime.ActiveHuntObjectives.Remove(key);
        }

        if (state.RetrievalRouteDeliveryActive)
        {
            state.RetrievalRouteDeliveryActive = false;
            _objectiveRuntime.ActiveRetrievalRouteDeliveries.Remove(key);
        }

        _objectiveRuntime.ActiveGhostRoleObjectives.Remove(key);

        state.RetrievalDeliveredEntities.Clear();
        state.RetrievalAcceptedCargoCount = 0;
        state.RetrievalLastAcceptedCargoCoordinates = null;
        state.RetrievalRouteDeliveryCompleted = false;
        state.HuntTargetWasKilled = false;
        state.GhostRoleSurvivalStart = null;
        state.GhostRoleSurvivalDeadline = null;
        state.GhostRoleSurvivalMind = null;
        state.GhostRoleSurvivalObjective = null;
        state.GhostRoleSurvivalSucceeded = false;
        state.LastKnownTargetCoordinates = null;
        _objectiveRuntime.ByContract.Remove(key);
    }

    private void CleanupGhostRoleSurvivalObjective(ObjectiveRuntimeState state)
    {
        if (state.GhostRoleSurvivalObjective is not { } objective || objective == EntityUid.Invalid)
            return;

        if (state.GhostRoleSurvivalSucceeded)
        {
            if (!TerminatingOrDeleted(objective) &&
                TryComp(objective, out NcContractGhostRoleSurvivalObjectiveComponent? survival))
            {
                survival.Finished = true;
                survival.Succeeded = true;
            }

            return;
        }

        if (state.GhostRoleSurvivalMind is { } mindId &&
            TryComp(mindId, out MindComponent? mind))
        {
            mind.Objectives.Remove(objective);
        }

        if (!TerminatingOrDeleted(objective))
            Del(objective);
    }

    private void CleanupRetrievalSpawnedEntities(ObjectiveRuntimeState state, bool deleteSpawnedEntities)
    {
        if (state.RetrievalSpawnedEntities.Count == 0)
        {
            state.RetrievalSpawnedEntitySet.Clear();
            return;
        }

        for (var i = state.RetrievalSpawnedEntities.Count - 1; i >= 0; i--)
        {
            var ent = state.RetrievalSpawnedEntities[i];
            _objectiveRuntime.ByRetrievalCargo.Remove(ent);

            if (deleteSpawnedEntities && ent != EntityUid.Invalid && !TerminatingOrDeleted(ent))
                Del(ent);
        }

        state.RetrievalSpawnedEntities.Clear();
        state.RetrievalSpawnedEntitySet.Clear();
    }

    private void CleanupHuntSpawnedTargets(ObjectiveRuntimeState state, bool deleteSpawnedTargets)
    {
        if (state.HuntSpawnedTargets.Count == 0)
            return;

        for (var i = state.HuntSpawnedTargets.Count - 1; i >= 0; i--)
        {
            var ent = state.HuntSpawnedTargets[i];
            if (deleteSpawnedTargets && ent != EntityUid.Invalid && !TerminatingOrDeleted(ent))
                Del(ent);
        }

        state.HuntSpawnedTargets.Clear();
    }

    private void CleanupSpawnedHuntBodyTarget(ObjectiveRuntimeState state, bool deleteBody)
    {
        if (state.HuntBodyEntity is not { } body || body == EntityUid.Invalid)
            return;

        state.HuntBodyEntity = null;
        RemoveSpawnedHuntTarget(state, body);

        if (deleteBody && !TerminatingOrDeleted(body))
            Del(body);
    }
}
