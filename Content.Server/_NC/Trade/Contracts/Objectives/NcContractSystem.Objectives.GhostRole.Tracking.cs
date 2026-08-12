using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryRetargetGhostRolePinpointersForOwners(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (state.PinpointerEntities.Count == 0)
            return false;

        if (!TryGetObjectiveContract(key, out _, out var contract) ||
            !contract.Taken ||
            contract.Runtime.Failed ||
            !contract.IsGhostRoleObjective ||
            !state.GhostRoleTaken)
            return false;

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return true;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            if (!TryGetPinpointerCarrier(pinpointer, out var carrier) ||
                !TryResolveGhostRolePinpointerTargetForUser(key.Store, carrier, contract, state, out var target) ||
                target == EntityUid.Invalid ||
                TerminatingOrDeleted(target))
                continue;

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }

        return true;
    }

    private bool TryResolveGhostRolePinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;
        if (!contract.IsGhostRoleObjective || !state.GhostRoleTaken)
            return false;

        if (state.TargetEntity is not { } tracked ||
            tracked == EntityUid.Invalid ||
            TerminatingOrDeleted(tracked))
            return false;

        if (IsGhostRoleTargetReadyForClaim(store, tracked, contract) ||
            IsGhostRoleTargetCarriedByUser(tracked, user))
        {
            target = store;
            return true;
        }

        target = tracked;
        return true;
    }

    private void ExpireUnacceptedGhostRoleObjective((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.GhostRoleTaken ||
            state.GhostRoleAcceptDeadline is not { } deadline ||
            _timing.CurTime < deadline)
            return;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, true);
            return;
        }

        if (!contract.Taken || !contract.IsGhostRoleObjective || contract.Completed)
            return;

        MarkGhostRoleRoundEndOutcome(
            state,
            GhostRoleRoundEndOutcome.NotAccepted,
            Loc.GetString("nc-store-contract-ghost-role-timeout"));
        RemoveObjectiveContractAndRefill(
            key,
            comp,
            true,
            false);
    }

    private void HandleGhostRoleTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            MarkGhostRoleRoundEndOutcome(
                state,
                GhostRoleRoundEndOutcome.TargetLost,
                Loc.GetString("nc-store-contract-ghost-role-target-lost"));
        }

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-target-lost"),
            ContractObjectiveOutcome.TargetLost);
    }

    public bool HasRealtimeContractState(NcStoreComponent comp)
    {
        foreach (var contract in comp.Contracts.Values)
        {
            if (!contract.Taken)
                continue;

            EnsureObjectiveRuntimeDefaults(contract);
            if (contract.Runtime.Failed || contract.Completed)
                continue;

            if (contract.IsGhostRoleObjective ||
                contract.IsTrackedDeliveryObjective ||
                contract.AllowsStoreWorldTurnIn)
                return true;
        }

        return false;
    }

    private bool IsGhostRoleTargetAtStore(EntityUid store, EntityUid target)
    {
        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(target, out TransformComponent? targetXform))
            return false;

        if (storeXform.MapID != targetXform.MapID)
            return false;

        var storePos = _xform.GetWorldPosition(storeXform);
        var targetPos = _xform.GetWorldPosition(targetXform);
        return (targetPos - storePos).LengthSquared() <=
            NcContractTuning.GhostRoleStoreDeliveryRange * NcContractTuning.GhostRoleStoreDeliveryRange;
    }
}
