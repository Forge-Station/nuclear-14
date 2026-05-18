using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

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
        state.HuntV2SpawnedTargets.Clear();
        state.HuntTargetWasKilled = false;
        state.LastKnownTargetCoordinates = null;

        ResetObjectiveState(contract);

        if (!TrySpawnHuntV2Targets(store, contractId, contract, state))
        {
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
            return false;
        }

        if (!state.HuntV2Active)
        {
            state.HuntV2Active = true;
            _activeHuntV2Objectives++;
        }

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

            if (!IsHuntV2SpawnedTarget(state, killedTarget) ||
                !IsMatchingHuntV2Target(killedTarget, contract, allowDeadTarget: true))
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
                !IsHuntV2SpawnedTarget(state, killedTarget) ||
                !IsMatchingHuntV2Target(killedTarget, contract, allowDeadTarget: true))
            {
                continue;
            }

            RemoveHuntV2SpawnedTarget(state, killedTarget);

            if (TryComp(killedTarget, out TransformComponent? killedXform))
                state.LastKnownTargetCoordinates = killedXform.Coordinates;

            SetObjectiveStage(contract, contract.Runtime.Stage + 1);
            if (!contract.Completed)
            {
                if (TryFindNearestLiveHuntV2Target(key.Store, contract, state, out var liveTarget))
                {
                    RetargetObjectivePinpointers(key, state, liveTarget);
                    continue;
                }

                FinalizeObjectiveFailure(
                    key,
                    comp,
                    contract,
                    Loc.GetString("nc-store-contract-hunt-target-lost"),
                    deleteGuards: false);
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

        if (TryFindNearestLiveHuntV2Target(store, contract, state, out var liveTarget))
        {
            target = liveTarget;
            return true;
        }

        target = store;
        return true;
    }

    private bool TryFindNearestLiveHuntV2Target(
        EntityUid origin,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;

        if (!TryComp(origin, out TransformComponent? originXform))
            return false;

        var originMap = _xform.ToMapCoordinates(originXform.Coordinates);
        var originPos = _xform.GetWorldPosition(originXform);
        var bestDistSq = float.MaxValue;

        for (var i = 0; i < state.HuntV2SpawnedTargets.Count; i++)
        {
            var candidate = state.HuntV2SpawnedTargets[i];
            if (candidate == EntityUid.Invalid || TerminatingOrDeleted(candidate))
                continue;

            if (!TryComp(candidate, out MobStateComponent? mobState) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
                continue;

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

    private bool TrySpawnHuntV2Targets(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        var targets = GetEffectiveTargets(contract);
        var required = Math.Max(1, CalculateTotalRequired(targets));
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var targetDef = targets[targetIndex];
            var targetRequired = Math.Max(0, targetDef.Required);
            if (targetRequired <= 0)
                continue;

            for (var i = 0; i < targetRequired; i++)
            {
                if (!TryResolveHuntV2SpawnPrototype(contractId, targetDef, out var targetProtoId))
                    return false;

                if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out var spawnCoords, fallbackToStore: false))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Hunt runtime init failed for '{contractId}': cannot resolve hunt spawn point.");
                    return false;
                }

                if (!TrySpawnObjectiveTarget(contractId, targetProtoId, spawnCoords, out var target))
                    return false;

                state.HuntV2SpawnedTargets.Add(target);
                if (state.LastKnownTargetCoordinates == null && TryComp(target, out TransformComponent? targetXform))
                    state.LastKnownTargetCoordinates = targetXform.Coordinates;
            }
        }

        return state.HuntV2SpawnedTargets.Count == required;
    }

    private bool TryResolveHuntV2SpawnPrototype(
        string contractId,
        ContractTargetServerData target,
        out string prototypeId)
    {
        prototypeId = string.Empty;

        if (target.MatchMode == PrototypeMatchMode.Exact)
        {
            prototypeId = target.TargetItem;
            return _prototypes.HasIndex<EntityPrototype>(prototypeId);
        }

        if (string.IsNullOrWhiteSpace(target.TargetItem) ||
            !_prototypes.TryIndex<NcHuntGroupPrototype>(target.TargetItem, out var group) ||
            group.Prototypes.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt runtime init failed for '{contractId}': target group has no spawnable prototypes.");
            return false;
        }

        var candidates = new List<string>(group.Prototypes.Count);
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var candidate = group.Prototypes[i];
            if (!string.IsNullOrWhiteSpace(candidate) && _prototypes.HasIndex<EntityPrototype>(candidate))
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt runtime init failed for '{contractId}': target group '{group.ID}' has no valid entity prototypes.");
            return false;
        }

        prototypeId = _random.Pick(candidates);
        return true;
    }

    private static bool IsHuntV2SpawnedTarget(ObjectiveRuntimeState state, EntityUid target)
    {
        for (var i = 0; i < state.HuntV2SpawnedTargets.Count; i++)
        {
            if (state.HuntV2SpawnedTargets[i] == target)
                return true;
        }

        return false;
    }

    private static void RemoveHuntV2SpawnedTarget(ObjectiveRuntimeState state, EntityUid target)
    {
        for (var i = state.HuntV2SpawnedTargets.Count - 1; i >= 0; i--)
        {
            if (state.HuntV2SpawnedTargets[i] == target)
                state.HuntV2SpawnedTargets.RemoveAt(i);
        }
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

        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            if (MatchesHuntV2TargetEntry(prototypeId, targets[i]))
                return true;
        }

        return false;
    }

    private bool MatchesHuntV2TargetEntry(string prototypeId, ContractTargetServerData target)
    {
        if (string.IsNullOrWhiteSpace(prototypeId) || string.IsNullOrWhiteSpace(target.TargetItem))
            return false;

        if (target.MatchMode == PrototypeMatchMode.Exact)
            return prototypeId == target.TargetItem;

        if (!_prototypes.TryIndex<NcHuntGroupPrototype>(target.TargetItem, out var group))
            return false;

        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            if (group.Prototypes[i] == prototypeId)
                return true;
        }

        return false;
    }
}
