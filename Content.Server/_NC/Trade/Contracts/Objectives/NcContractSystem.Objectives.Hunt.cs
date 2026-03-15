using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void OnObjectiveTrackedMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        if (!_objectiveRuntimeByTarget.TryGetValue(args.Target, out var key))
            return;

        if (!TryGetObjectiveContract(key, out _, out var contract) || !contract.IsHuntObjective)
            return;

        OnObjectiveTrackedTargetResolved(key, args.Target);
    }

    private void HandleHuntObjectiveTargetResolved(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract
    ) =>
        FinalizeObjectiveCompletion(key, contract);

    private void SyncHuntObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid)
            return;

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return;
        }

        if (TryComp(target, out MobStateComponent? mobState))
        {
            if (mobState.CurrentState == MobState.Dead)
                OnObjectiveTrackedTargetResolved(key, target);

            return;
        }

        if (TryComp(target, out TransformComponent? targetXform) && IsTargetInEntityContainer(targetXform))
            OnObjectiveTrackedTargetResolved(key, target);
    }
}
