using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void OnObjectiveTrackedMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        TryHandleSpawnedHuntTargetKilled(args.Target);

        if (!_objectiveRuntime.ByTarget.TryGetValue(args.Target, out var key))
            return;

        if (!TryGetObjectiveContract(key, out _, out var contract) || !contract.IsHuntObjective)
            return;

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        state.HuntTargetWasKilled = true;
        if (TryComp(args.Target, out TransformComponent? targetXform))
            state.LastKnownTargetCoordinates = targetXform.Coordinates;

        OnObjectiveTrackedTargetResolved(key, args.Target);
    }

    private void HandleHuntObjectiveTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        var targetWasKilled = state.HuntTargetWasKilled;
        state.HuntTargetWasKilled = false;

        if (!targetWasKilled)
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                Loc.GetString("nc-store-contract-hunt-target-lost"),
                deleteGuards: false);
            return;
        }

        var runtime = contract.Runtime;
        var stageGoal = Math.Max(1, runtime.StageGoal);
        if (runtime.Stage >= stageGoal)
        {
            FinalizeObjectiveCompletion(key, contract);
            return;
        }

        SetObjectiveStage(contract, runtime.Stage + 1);

        if (runtime.Stage >= stageGoal)
        {
            var completionCoords = ResolveHuntObjectiveCompletionCoordinates(key.Store, state);
            if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, completionCoords))
                return;

            FinalizeObjectiveCompletion(key, contract);
            return;
        }

        if (TrySpawnNextHuntObjectiveTarget(key, contract, state))
            return;

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-hunt-next-target-spawn-failed"),
            deleteGuards: false);
    }

    private bool TrySpawnNextHuntObjectiveTarget(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        var requestedTargetId = ResolveHuntObjectiveRequestedTargetId(contract);
        if (!TryResolveTrackedObjectiveSpawnPrototype(
            key.ContractId,
            contract,
            requestedTargetId,
            true,
            out var targetProtoId))
            return false;

        if (!TryResolveObjectiveSpawnCoordinates(key.Store, contract.Config, out var spawnCoords))
            return false;

        if (!TrySpawnObjectiveTarget(key.ContractId, targetProtoId, spawnCoords, out var target))
            return false;

        RegisterObjectiveTarget(key, state, target);
        contract.Config.TargetPrototype = targetProtoId;

        var config = contract.Config;
        if (config.GuardCount > 0 &&
            !string.IsNullOrWhiteSpace(config.GuardPrototype) &&
            !TrySpawnObjectiveGuards(key, state, config, spawnCoords))
            Sawmill.Warning($"[Contracts] Hunt stage guard wave failed for '{key.ContractId}'.");

        RetargetObjectivePinpointers(key, state, target);
        return true;
    }

    private static string ResolveHuntObjectiveRequestedTargetId(ContractServerData contract) =>
        !string.IsNullOrWhiteSpace(contract.TargetItem)
            ? contract.TargetItem
            : contract.Config.TargetPrototype;

    private EntityCoordinates ResolveHuntObjectiveCompletionCoordinates(EntityUid store, ObjectiveRuntimeState state)
    {
        if (state.LastKnownTargetCoordinates is { } targetCoords && targetCoords != EntityCoordinates.Invalid)
            return targetCoords;

        if (TryComp(store, out TransformComponent? storeXform))
            return storeXform.Coordinates;

        return EntityCoordinates.Invalid;
    }

    private void SyncHuntObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        if (IsSpawnedHuntContract(contract))
            return;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid)
            return;

        if (TryComp(target, out TransformComponent? trackedTargetXform))
            state.LastKnownTargetCoordinates = trackedTargetXform.Coordinates;

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return;
        }

        if (TryComp(target, out MobStateComponent? mobState))
        {
            if (mobState.CurrentState == MobState.Dead)
            {
                state.HuntTargetWasKilled = true;
                if (TryComp(target, out TransformComponent? liveTargetXform))
                    state.LastKnownTargetCoordinates = liveTargetXform.Coordinates;
                OnObjectiveTrackedTargetResolved(key, target);
            }

            return;
        }

        if (TryComp(target, out TransformComponent? containerTargetXform) &&
            IsTargetInEntityContainer(containerTargetXform))
            OnObjectiveTrackedTargetResolved(key, target);
    }
}
