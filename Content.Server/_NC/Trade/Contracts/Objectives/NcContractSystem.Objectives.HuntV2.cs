using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryInitializeHuntObjectiveRuntimeOnTake(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract)
    {
        if (!IsHuntV2Contract(contract))
            return TryInitializeHuntObjective(store, user, contractId, contract);

        return TryInitializeHuntV2Objective(store, user, contractId, contract);
    }

    private static bool IsHuntV2Contract(ContractServerData contract)
    {
        return contract.IsHuntObjective && contract.Config.HuntV2Enabled;
    }

    private bool TryInitializeHuntV2Objective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract)
    {
        if (contract.Config.HuntV2CompletionMode != NcHuntCompletionMode.TrophyTurnIn)
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt runtime init failed for '{contractId}': only TrophyTurnIn is supported.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(contract.Config.ProofPrototype))
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt runtime init failed for '{contractId}': TrophyTurnIn requires proof prototype.");
            return false;
        }

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = null;
        state.HuntTargetWasKilled = false;
        state.LastKnownTargetCoordinates = null;

        if (!state.HuntV2Active)
        {
            state.HuntV2Active = true;
            _activeHuntV2Objectives++;
        }

        ResetObjectiveState(contract);

        if (!contract.Config.GivePinpointer)
            return true;

        if (!TryResolveHuntV2PinpointerTarget(store, contract, state, out var pinpointerTarget))
            return false;

        var spawnCoords = EntityCoordinates.Invalid;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(user, out TransformComponent? userXform))
            spawnCoords = userXform.Coordinates;

        if (spawnCoords == EntityCoordinates.Invalid &&
            TryComp(pinpointerTarget, out TransformComponent? targetXform))
        {
            spawnCoords = targetXform.Coordinates;
        }

        if (spawnCoords == EntityCoordinates.Invalid)
            return false;

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, contract.Config, spawnCoords);
    }

    private void TryHandleHuntV2TargetKilled(EntityUid killedTarget)
    {
        if (killedTarget == EntityUid.Invalid || TerminatingOrDeleted(killedTarget))
            return;

        if (_objectiveRuntimeByContract.Count == 0)
            return;

        List<(EntityUid Store, string ContractId)>? candidates = null;
        foreach (var (key, state) in _objectiveRuntimeByContract)
        {
            if (!state.HuntV2Active)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                contract.Completed ||
                !IsHuntV2Contract(contract))
            {
                continue;
            }

            if (!IsMatchingHuntV2Target(killedTarget, contract, allowDeadTarget: true))
                continue;

            candidates ??= new();
            candidates.Add(key);
        }

        if (candidates == null || candidates.Count == 0)
            return;

        for (var i = 0; i < candidates.Count; i++)
        {
            var key = candidates[i];
            if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
                continue;

            if (!TryGetObjectiveContract(key, out var comp, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                contract.Completed ||
                !IsHuntV2Contract(contract) ||
                !IsMatchingHuntV2Target(killedTarget, contract, allowDeadTarget: true))
            {
                continue;
            }

            if (TryComp(killedTarget, out TransformComponent? killedXform))
                state.LastKnownTargetCoordinates = killedXform.Coordinates;

            SetObjectiveStage(contract, contract.Runtime.Stage + 1);
            if (!contract.Completed)
            {
                if (TryResolveHuntV2PinpointerTarget(key.Store, contract, state, out var liveTarget))
                    RetargetObjectivePinpointers(key, state, liveTarget);

                continue;
            }

            var completionCoords = ResolveHuntObjectiveCompletionCoordinates(key.Store, state);
            if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, completionCoords))
                continue;

            FinalizeObjectiveCompletion(key, contract);
        }
    }

    private void UpdateHuntV2PinpointerTargets()
    {
        if (_activeHuntV2Objectives <= 0)
            return;

        foreach (var (key, state) in _objectiveRuntimeByContract)
        {
            if (!state.HuntV2Active || state.PinpointerEntities.Count == 0)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                !IsHuntV2Contract(contract))
            {
                continue;
            }

            if (TryResolveHuntV2PinpointerTarget(key.Store, contract, state, out var target))
                RetargetObjectivePinpointers(key, state, target);
        }
    }

    private bool TryResolveHuntV2PinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!IsHuntV2Contract(contract))
            return false;

        if (contract.Completed)
        {
            if (state.ProofEntity is { } proof &&
                proof != EntityUid.Invalid &&
                !TerminatingOrDeleted(proof))
            {
                if (TryComp(proof, out TransformComponent? proofXform) &&
                    IsTargetInEntityContainer(proofXform))
                {
                    target = store;
                    return true;
                }

                target = proof;
                return true;
            }

            target = store;
            return true;
        }

        if (TryFindNearestLiveHuntV2Target(store, contract, out var liveTarget))
        {
            target = liveTarget;
            return true;
        }

        target = store;
        return true;
    }

    private bool TryFindNearestLiveHuntV2Target(EntityUid origin, ContractServerData contract, out EntityUid target)
    {
        target = EntityUid.Invalid;

        if (!TryComp(origin, out TransformComponent? originXform))
            return false;

        var originMap = _xform.ToMapCoordinates(originXform.Coordinates);
        var originPos = _xform.GetWorldPosition(originXform);
        var bestDistSq = float.MaxValue;

        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var candidate, out var mobState, out var candidateXform))
        {
            if (mobState.CurrentState == MobState.Dead)
                continue;

            if (!IsMatchingHuntV2Target(candidate, contract, allowDeadTarget: false))
                continue;

            var candidateMap = _xform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != originMap.MapId)
                continue;

            var candidatePos = _xform.GetWorldPosition(candidateXform);
            var distSq = (candidatePos - originPos).LengthSquared();
            if (distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            target = candidate;
        }

        return target != EntityUid.Invalid;
    }

    private bool IsMatchingHuntV2Target(EntityUid entity, ContractServerData contract, bool allowDeadTarget)
    {
        if (entity == EntityUid.Invalid || TerminatingOrDeleted(entity))
            return false;

        if (!TryComp(entity, out MobStateComponent? mobState))
            return false;

        if (!allowDeadTarget && mobState.CurrentState == MobState.Dead)
            return false;

        if (!TryGetPlanningEntityPrototypeId(entity, out var prototypeId))
            return false;

        var config = contract.Config;
        if (!string.IsNullOrWhiteSpace(config.HuntV2TargetPrototype))
            return prototypeId == config.HuntV2TargetPrototype;

        if (string.IsNullOrWhiteSpace(config.HuntV2TargetGroup))
            return false;

        if (!_prototypes.TryIndex<NcHuntGroupPrototype>(config.HuntV2TargetGroup, out var group))
            return false;

        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            if (group.Prototypes[i] == prototypeId)
                return true;
        }

        return false;
    }
}
