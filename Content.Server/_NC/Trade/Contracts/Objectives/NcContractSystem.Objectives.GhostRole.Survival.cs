using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryCompleteGhostRoleSurvivalObjective(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (contract.Config.GhostRoleSurvivalDurationSeconds <= 0 ||
            state.GhostRoleSurvivalSucceeded ||
            state.GhostRoleSurvivalDeadline is not { } deadline ||
            _timing.CurTime < deadline ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(target) ||
            _contractGhostRoleRotting.IsRotten(target) ||
            !IsGhostRoleTargetAlive(target) ||
            IsGhostRoleCompletionSatisfied(key.Store, target, contract))
            return false;

        state.GhostRoleSurvivalSucceeded = true;
        MarkGhostRoleRoundEndOutcome(
            state,
            GhostRoleRoundEndOutcome.RoleSurvived,
            Loc.GetString("nc-store-contract-ghost-role-survival-succeeded"));
        if (state.GhostRoleSurvivalObjective is { } objective &&
            !TerminatingOrDeleted(objective) &&
            TryComp(objective, out NcContractGhostRoleSurvivalObjectiveComponent? survival))
        {
            survival.Finished = true;
            survival.Succeeded = true;
        }

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-survival-succeeded"),
            ContractObjectiveOutcome.RoleSurvived,
            false);
        return true;
    }

    // Ghost role objective runtime.
    private void UpdateGhostRoleObjectiveTimeouts()
    {
        if (_objectiveRuntime.ActiveGhostRoleObjectives.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ActiveGhostRoleObjectives)
            _objectiveRuntime.KeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
        {
            var key = _objectiveRuntime.KeysScratch[i];
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            {
                _objectiveRuntime.ActiveGhostRoleObjectives.Remove(key);
                continue;
            }

            if (state.GhostRoleTaken)
            {
                if (TryGetObjectiveContract(key, out var comp, out var contract) &&
                    TryFailGhostRoleTargetIfInvalidOrRotten(key, state, comp, contract))
                    continue;

                if (TryCompleteGhostRoleSurvivalObjective(key, state, comp, contract))
                    continue;

                TryRetargetGhostRolePinpointersForOwners(key, state);
                continue;
            }

            if (state.GhostRoleAcceptDeadline is not { } deadline)
                continue;

            if (_timing.CurTime >= deadline)
                ExpireUnacceptedGhostRoleObjective(key);
        }

        _objectiveRuntime.KeysScratch.Clear();
    }
}
