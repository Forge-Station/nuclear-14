using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void TryHandleSpawnedHuntTargetKilled(EntityUid killedTarget)
    {
        if (killedTarget == EntityUid.Invalid || TerminatingOrDeleted(killedTarget))
            return;

        if (_objectiveRuntime.ActiveHuntObjectives.Count == 0)
            return;

        List<(EntityUid Store, string ContractId)>? candidates = null;
        foreach (var key in _objectiveRuntime.ActiveHuntObjectives)
        {
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
                continue;

            if (!state.HuntActive)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                contract.Completed ||
                !IsSpawnedHuntContract(contract))
                continue;

            if (!IsSpawnedHuntTarget(state, killedTarget) ||
                !IsMatchingSpawnedHuntTarget(killedTarget, contract, true))
                continue;

            candidates ??= new();
            candidates.Add(key);
        }

        if (candidates == null || candidates.Count == 0)
            return;

        for (var i = 0; i < candidates.Count; i++)
        {
            var key = candidates[i];
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
                continue;

            if (!TryGetObjectiveContract(key, out var comp, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                contract.Completed ||
                !IsSpawnedHuntContract(contract) ||
                !IsSpawnedHuntTarget(state, killedTarget) ||
                !IsMatchingSpawnedHuntTarget(killedTarget, contract, true))
                continue;

            RemoveSpawnedHuntTarget(state, killedTarget);

            if (TryComp(killedTarget, out TransformComponent? killedXform))
                state.LastKnownTargetCoordinates = killedXform.Coordinates;

            TryAdvanceSpawnedHuntTargetProgress(killedTarget, contract, state);
            SetObjectiveStage(contract, CalculateSpawnedHuntTotalProgress(contract));
            if (!contract.Completed)
            {
                if (TryFindNearestLiveSpawnedHuntTarget(key.Store, contract, state, out var liveTarget))
                {
                    RetargetObjectivePinpointers(key, state, liveTarget);
                    continue;
                }

                FinalizeObjectiveTerminalOutcome(
                    key,
                    comp,
                    contract,
                    Loc.GetString("nc-store-contract-hunt-target-lost"),
                    deleteGuards: false);
                continue;
            }

            if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.TrophyTurnIn)
            {
                var completionCoords = ResolveHuntObjectiveCompletionCoordinates(key.Store, state);
                if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, completionCoords))
                    continue;
            }
            else if (!TryGetHuntBodyEntity(state, out _))
            {
                FinalizeObjectiveTerminalOutcome(
                    key,
                    comp,
                    contract,
                    Loc.GetString("nc-store-contract-hunt-target-lost"),
                    deleteGuards: false);
                continue;
            }

            FinalizeObjectiveCompletion(key, contract);
        }
    }

    private void UpdateSpawnedHuntPinpointerTargets()
    {
        if (_objectiveRuntime.ActiveHuntObjectives.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ActiveHuntObjectives)
            _objectiveRuntime.KeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
        {
            var key = _objectiveRuntime.KeysScratch[i];
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            {
                _objectiveRuntime.ActiveHuntObjectives.Remove(key);
                continue;
            }

            if (!state.HuntActive || state.PinpointerEntities.Count == 0)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                !IsSpawnedHuntContract(contract))
                continue;

            if (TryRetargetSpawnedHuntCompletedPinpointersForOwners(key, contract, state))
                continue;

            if (TryResolveSpawnedHuntPinpointerTarget(key.Store, contract, state, out var target))
                RetargetObjectivePinpointers(key, state, target);
        }

        _objectiveRuntime.KeysScratch.Clear();
    }

    private void TryHandleHuntBodyEntityTerminating(EntityUid body)
    {
        if (body == EntityUid.Invalid || _objectiveRuntime.ActiveHuntObjectives.Count == 0)
            return;

        List<(EntityUid Store, string ContractId)>? candidates = null;
        foreach (var key in _objectiveRuntime.ActiveHuntObjectives)
        {
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
                continue;

            if (!state.HuntActive || state.HuntBodyEntity != body)
                continue;

            candidates ??= new();
            candidates.Add(key);
        }

        if (candidates == null)
            return;

        for (var i = 0; i < candidates.Count; i++)
        {
            var key = candidates[i];
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
                state.HuntBodyEntity != body)
                continue;

            state.HuntBodyEntity = null;
            RemoveSpawnedHuntTarget(state, body);

            if (!TryGetObjectiveContract(key, out var comp, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                contract.Completed && !RequiresSpawnedHuntBodyTurnIn(contract))
                continue;

            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                Loc.GetString("nc-store-contract-hunt-target-lost"),
                deleteGuards: false);
        }
    }
}
