using Content.Shared._NC.Trade;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void SyncGhostRoleObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        var runtime = contract.Runtime;

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            runtime.GhostRolePendingAcceptance = false;
            runtime.AcceptTimeoutRemainingSeconds = 0;
            runtime.GhostRoleSurvivalRemainingSeconds = 0;
            runtime.StatusHint = string.Empty;
            return;
        }

        if (!state.GhostRoleTaken && state.GhostRoleAcceptDeadline is { } deadline)
        {
            runtime.GhostRolePendingAcceptance = true;
            runtime.AcceptTimeoutRemainingSeconds = Math.Max(
                0,
                (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds));
            runtime.GhostRoleSurvivalRemainingSeconds = 0;
            runtime.StatusHint = Loc.GetString("nc-store-contract-ghost-role-hint-waiting");
            runtime.Stage = 0;
            return;
        }

        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        SyncGhostRoleSurvivalRemaining(state, runtime);

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid)
        {
            runtime.StatusHint = string.Empty;
            return;
        }

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return;
        }

        if (TryGetObjectiveContract(key, out var comp, out var liveContract) &&
            TryFailGhostRoleTargetIfInvalidOrRotten(key, state, comp, liveContract))
            return;

        runtime.Stage = state.GhostRoleTaken && IsGhostRoleCompletionSatisfied(store, target, contract)
            ? Math.Max(1, runtime.StageGoal)
            : 0;
        runtime.StatusHint = BuildGhostRoleObjectiveStatusHint(store, target, contract);

        TryRetargetGhostRolePinpointersForOwners(key, state);
    }

    private void SyncGhostRoleSurvivalRemaining(ObjectiveRuntimeState state, ContractRuntimeContextData runtime)
    {
        runtime.GhostRoleSurvivalRemainingSeconds = 0;

        if (!state.GhostRoleTaken ||
            state.GhostRoleSurvivalSucceeded ||
            state.GhostRoleSurvivalDeadline is not { } deadline)
            return;

        runtime.GhostRoleSurvivalRemainingSeconds = Math.Max(
            0,
            (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds));
    }

    private string BuildGhostRoleObjectiveStatusHint(EntityUid store, EntityUid target, ContractServerData contract)
    {
        if (_contractGhostRoleRotting.IsRotten(target))
            return Loc.GetString("nc-store-contract-ghost-role-target-rotten");

        return contract.Config.GhostRoleCompletionMode switch
        {
            NcGhostRoleCompletionMode.AliveCuffedTurnIn => BuildAliveCuffedGhostRoleStatusHint(store, target),
            NcGhostRoleCompletionMode.DeadBodyTurnIn => BuildDeadBodyGhostRoleStatusHint(store, target),
            _ => Loc.GetString("nc-store-contract-ghost-role-hint-deliver")
        };
    }

    private string BuildAliveCuffedGhostRoleStatusHint(EntityUid store, EntityUid target)
    {
        if (!IsGhostRoleTargetAlive(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-alive-revive");

        if (!IsGhostRoleTargetCuffed(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-alive-cuff");

        if (!IsGhostRoleTargetFullyHealed(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-alive-heal");

        if (!IsGhostRoleTargetAtStore(store, target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-deliver");

        return Loc.GetString("nc-store-contract-ghost-role-hint-alive-ready");
    }

    private string BuildDeadBodyGhostRoleStatusHint(EntityUid store, EntityUid target)
    {
        if (!IsGhostRoleTargetDead(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-dead-kill");

        if (!IsGhostRoleTargetAtStore(store, target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-dead-deliver");

        return Loc.GetString("nc-store-contract-ghost-role-hint-dead-ready");
    }

    private bool TryFailGhostRoleTargetIfInvalidOrRotten(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (!state.GhostRoleTaken ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid)
            return false;

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return true;
        }

        if (!_contractGhostRoleRotting.IsRotten(target))
            return false;

        MarkGhostRoleRoundEndOutcome(
            state,
            GhostRoleRoundEndOutcome.TargetRotten,
            Loc.GetString("nc-store-contract-ghost-role-target-rotten"));
        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-target-rotten"),
            ContractObjectiveOutcome.TargetRotten);
        return true;
    }

    private bool IsGhostRoleTargetReadyForClaim(EntityUid store, EntityUid target, ContractServerData contract) =>
        !TerminatingOrDeleted(target) &&
        !_contractGhostRoleRotting.IsRotten(target) &&
        IsGhostRoleCompletionSatisfied(store, target, contract);

    private bool IsGhostRoleSelfClaim(
        EntityUid store,
        string contractId,
        EntityUid user,
        ContractServerData contract
    )
    {
        if (!contract.IsGhostRoleObjective ||
            !_objectiveRuntime.ByContract.TryGetValue((store, contractId), out var state) ||
            !state.GhostRoleTaken ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid)
            return false;

        if (target == user)
            return true;

        return _contractMind.TryGetMind(target, out var targetMindId, out _) &&
            _contractMind.TryGetMind(user, out var userMindId, out _) &&
            targetMindId == userMindId;
    }

    private bool IsGhostRoleCompletionSatisfied(EntityUid store, EntityUid target, ContractServerData contract)
    {
        if (!IsGhostRoleTargetAtStore(store, target))
            return false;

        return contract.Config.GhostRoleCompletionMode switch
        {
            NcGhostRoleCompletionMode.DeadBodyTurnIn => IsGhostRoleTargetDead(target),
            NcGhostRoleCompletionMode.AliveCuffedTurnIn => IsGhostRoleTargetAlive(target) &&
                IsGhostRoleTargetCuffed(target) &&
                IsGhostRoleTargetFullyHealed(target),
            _ => false
        };
    }

    private bool IsGhostRoleTargetDead(EntityUid target) =>
        TryComp(target, out MobStateComponent? mobState) &&
        mobState.CurrentState == MobState.Dead;

    private bool IsGhostRoleTargetAlive(EntityUid target) =>
        TryComp(target, out MobStateComponent? mobState) &&
        mobState.CurrentState != MobState.Dead;

    private bool IsGhostRoleTargetCuffed(EntityUid target) =>
        TryComp(target, out CuffableComponent? cuffable) &&
        _contractGhostRoleCuffs.IsCuffed((target, cuffable));

    private bool IsGhostRoleTargetFullyHealed(EntityUid target) =>
        TryComp(target, out DamageableComponent? damageable) &&
        damageable.TotalDamage <= FixedPoint2.Zero;

    private void OnObjectiveTrackedDamageChanged(EntityUid uid, DamageableComponent component, DamageChangedEvent args)
    {
        if (!_objectiveRuntime.ByTarget.TryGetValue(uid, out var key))
            return;

        if (!TryGetObjectiveContract(key, out _, out var contract) ||
            contract.ExecutionKind != ContractExecutionKind.GhostRoleObjective)
            return;

        UpdateObjectiveContractProgress(key.Store, key.ContractId, contract);

        var ev = new NcContractsChangedEvent();
        RaiseLocalEvent(key.Store, ref ev);
    }

    private bool IsGhostRoleTargetCarriedByUser(EntityUid target, EntityUid user)
    {
        if (TryComp(target, out PullableComponent? pullable) && pullable.Puller == user)
            return true;

        return TryGetContainedEntityRoot(target, out var root) && root == user;
    }
}
