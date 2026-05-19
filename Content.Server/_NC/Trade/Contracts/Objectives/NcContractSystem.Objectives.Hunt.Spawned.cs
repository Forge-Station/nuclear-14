using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
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
        if (!IsSpawnedHuntContract(contract))
            return TryInitializeHuntObjective(store, user, contractId, contract);

        return TryInitializeSpawnedHuntObjective(store, user, contractId, contract);
    }

    private static bool IsSpawnedHuntContract(ContractServerData contract)
    {
        return contract.IsHuntObjective && contract.Config.HuntEnabled;
    }

    private static bool RequiresSpawnedHuntBodyTurnIn(ContractServerData contract)
    {
        return IsSpawnedHuntContract(contract) &&
               contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn;
    }

    private bool TryInitializeSpawnedHuntObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract)
    {
        if (contract.Config.HuntCompletionMode is not (NcHuntCompletionMode.TrophyTurnIn or NcHuntCompletionMode.BodyTurnIn))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': only TrophyTurnIn and BodyTurnIn are supported.");
            return false;
        }

        if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.TrophyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.ProofPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': TrophyTurnIn requires proof prototype.");
            return false;
        }

        if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.HuntBodyPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': BodyTurnIn requires a body target.");
            return false;
        }

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = null;
        state.HuntBodyEntity = null;
        state.HuntSpawnedTargets.Clear();
        state.HuntTargetWasKilled = false;
        state.LastKnownTargetCoordinates = null;

        ResetObjectiveState(contract);

        if (!TrySpawnHuntTargets(store, contractId, contract, state))
        {
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
            return false;
        }

        if (!state.HuntActive)
        {
            state.HuntActive = true;
            _activeHuntObjectives.Add((store, contractId));
        }

        if (!contract.Config.GivePinpointer)
            return true;

        if (!TryResolveSpawnedHuntPinpointerTargetForUser(store, user, contract, state, out var pinpointerTarget))
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

    private void TryHandleSpawnedHuntTargetKilled(EntityUid killedTarget)
    {
        if (killedTarget == EntityUid.Invalid || TerminatingOrDeleted(killedTarget))
            return;

        if (_activeHuntObjectives.Count == 0)
            return;

        List<(EntityUid Store, string ContractId)>? candidates = null;
        foreach (var key in _activeHuntObjectives)
        {
            if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
                continue;

            if (!state.HuntActive)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                contract.Completed ||
                !IsSpawnedHuntContract(contract))
            {
                continue;
            }

            if (!IsSpawnedHuntTarget(state, killedTarget) ||
                !IsMatchingSpawnedHuntTarget(killedTarget, contract, allowDeadTarget: true))
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
                !IsSpawnedHuntContract(contract) ||
                !IsSpawnedHuntTarget(state, killedTarget) ||
                !IsMatchingSpawnedHuntTarget(killedTarget, contract, allowDeadTarget: true))
            {
                continue;
            }

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

                FinalizeObjectiveFailure(
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
                FinalizeObjectiveFailure(
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
        if (_activeHuntObjectives.Count == 0)
            return;

        _objectiveRuntimeKeysScratch.Clear();
        foreach (var key in _activeHuntObjectives)
            _objectiveRuntimeKeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
        {
            var key = _objectiveRuntimeKeysScratch[i];
            if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            {
                _activeHuntObjectives.Remove(key);
                continue;
            }

            if (!state.HuntActive || state.PinpointerEntities.Count == 0)
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                !IsSpawnedHuntContract(contract))
            {
                continue;
            }

            if (TryRetargetSpawnedHuntCompletedPinpointersForOwners(key, contract, state))
                continue;

            if (TryResolveSpawnedHuntPinpointerTarget(key.Store, contract, state, out var target))
                RetargetObjectivePinpointers(key, state, target);
        }

        _objectiveRuntimeKeysScratch.Clear();
    }

    private void TryHandleHuntBodyEntityTerminating(EntityUid body)
    {
        if (body == EntityUid.Invalid || _activeHuntObjectives.Count == 0)
            return;

        List<(EntityUid Store, string ContractId)>? candidates = null;
        foreach (var key in _activeHuntObjectives)
        {
            if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
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
            if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
                state.HuntBodyEntity != body)
            {
                continue;
            }

            state.HuntBodyEntity = null;
            RemoveSpawnedHuntTarget(state, body);

            if (!TryGetObjectiveContract(key, out var comp, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                (contract.Completed && !RequiresSpawnedHuntBodyTurnIn(contract)))
            {
                continue;
            }

            FinalizeObjectiveFailure(
                key,
                comp,
                contract,
                Loc.GetString("nc-store-contract-hunt-target-lost"),
                deleteGuards: false);
        }
    }

    private bool TryResolveSpawnedHuntPinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!IsSpawnedHuntContract(contract))
            return false;

        if (!contract.Completed)
            return TryResolveSpawnedHuntPinpointerTarget(store, contract, state, out target);

        if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn &&
            TryGetHuntBodyEntity(state, out var body))
        {
            target = IsSpawnedHuntBodyCarriedByUser(body, user) ? store : body;
            return true;
        }

        if (state.ProofEntity is { } proof &&
            proof != EntityUid.Invalid &&
            !TerminatingOrDeleted(proof))
        {
            target = TryGetContainedEntityRoot(proof, out var proofCarrier) && proofCarrier == user
                ? store
                : proof;
            return true;
        }

        target = store;
        return true;
    }

    private bool TryRetargetSpawnedHuntCompletedPinpointersForOwners(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!contract.Completed || state.PinpointerEntities.Count == 0)
            return false;

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return true;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            if (!_objectiveRuntimePinpointerOwners.TryGetValue(pinpointer, out var owner) ||
                !TryResolveSpawnedHuntPinpointerTargetForUser(key.Store, owner, contract, state, out var target) ||
                target == EntityUid.Invalid ||
                TerminatingOrDeleted(target))
            {
                continue;
            }

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }

        return true;
    }

    private bool TryResolveSpawnedHuntPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!IsSpawnedHuntContract(contract))
            return false;

        if (contract.Completed)
        {
            if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn &&
                TryGetHuntBodyEntity(state, out var body))
            {
                target = body;
                return true;
            }

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

        if (TryFindNearestLiveSpawnedHuntTarget(store, contract, state, out var liveTarget))
        {
            target = liveTarget;
            return true;
        }

        target = store;
        return true;
    }

    private bool TryFindNearestLiveSpawnedHuntTarget(
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

        for (var i = 0; i < state.HuntSpawnedTargets.Count; i++)
        {
            var candidate = state.HuntSpawnedTargets[i];
            if (candidate == EntityUid.Invalid || TerminatingOrDeleted(candidate))
                continue;

            if (!TryComp(candidate, out MobStateComponent? mobState) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
                continue;

            if (mobState.CurrentState == MobState.Dead)
                continue;

            if (!IsMatchingSpawnedHuntTarget(candidate, contract, allowDeadTarget: false))
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

    private bool TrySpawnHuntTargets(
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
                if (!TryResolveSpawnedHuntPrototype(contractId, targetDef, out var targetProtoId))
                    return false;

                if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out var spawnCoords, fallbackToStore: false))
                {
                    Sawmill.Warning(
                        $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve hunt spawn point.");
                    return false;
                }

                if (!TrySpawnObjectiveTarget(contractId, targetProtoId, spawnCoords, out var target))
                    return false;

                state.HuntSpawnedTargets.Add(target);
                if (targetDef.BodyRequired)
                    state.HuntBodyEntity = target;

                if (state.LastKnownTargetCoordinates == null && TryComp(target, out TransformComponent? targetXform))
                    state.LastKnownTargetCoordinates = targetXform.Coordinates;
            }
        }

        return state.HuntSpawnedTargets.Count == required;
    }

    private bool TryAdvanceSpawnedHuntTargetProgress(
        EntityUid killedTarget,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!TryGetPlanningEntityPrototypeId(killedTarget, out var prototypeId))
            return false;

        var targets = GetEffectiveTargets(contract);
        if (state.HuntBodyEntity == killedTarget)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (!target.BodyRequired || target.Progress >= target.Required)
                    continue;

                if (!MatchesSpawnedHuntTargetEntry(prototypeId, target))
                    continue;

                target.Progress = Math.Min(target.Required, target.Progress + 1);
                targets[i] = target;
                return true;
            }
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target.Progress >= target.Required)
                continue;

            if (!MatchesSpawnedHuntTargetEntry(prototypeId, target))
                continue;

            target.Progress = Math.Min(target.Required, target.Progress + 1);
            targets[i] = target;
            return true;
        }

        return false;
    }

    private static int CalculateSpawnedHuntTotalProgress(ContractServerData contract)
    {
        var progress = 0;
        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
            progress = SaturatingAdd(progress, Math.Max(0, targets[i].Progress));

        return progress;
    }

    private bool TryGetHuntBodyEntity(ObjectiveRuntimeState state, out EntityUid body)
    {
        body = EntityUid.Invalid;
        if (state.HuntBodyEntity is not { } candidate ||
            candidate == EntityUid.Invalid ||
            TerminatingOrDeleted(candidate))
        {
            return false;
        }

        if (!TryComp(candidate, out MobStateComponent? mobState) ||
            mobState.CurrentState != MobState.Dead)
        {
            return false;
        }

        body = candidate;
        return true;
    }

    private bool TryConsumeSpawnedHuntBodyTurnIn(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!RequiresSpawnedHuntBodyTurnIn(contract))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            !TryGetHuntBodyEntity(state, out var body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' requires the marked corpse to be brought back to the store.");
            return false;
        }

        if (!IsSpawnedHuntBodyInTurnInScope(store, user, body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' body is not being dragged by the claimant and is not near the store.");
            return false;
        }

        state.HuntBodyEntity = null;
        RemoveSpawnedHuntTarget(state, body);
        if (EntityManager.EntityExists(body))
            Del(body);

        return true;
    }

    private bool IsSpawnedHuntBodyInTurnInScope(EntityUid store, EntityUid user, EntityUid body)
    {
        if (IsSpawnedHuntBodyCarriedByUser(body, user))
            return true;

        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(body, out TransformComponent? bodyXform) ||
            IsTargetInEntityContainer(bodyXform))
        {
            return false;
        }

        var storeMap = _xform.ToMapCoordinates(storeXform.Coordinates);
        var bodyMap = _xform.ToMapCoordinates(bodyXform.Coordinates);
        if (storeMap.MapId != bodyMap.MapId)
            return false;

        var delta = _xform.GetWorldPosition(storeXform) - _xform.GetWorldPosition(bodyXform);
        return delta.LengthSquared() <=
               NcContractTuning.TrackedDeliveryStoreRange * NcContractTuning.TrackedDeliveryStoreRange;
    }

    private bool IsSpawnedHuntBodyCarriedByUser(EntityUid body, EntityUid user)
    {
        if (TryComp(body, out PullableComponent? pullable) && pullable.Puller == user)
            return true;

        return TryGetContainedEntityRoot(body, out var root) && root == user;
    }

    private bool TryResolveSpawnedHuntPrototype(
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
                $"[Contracts] Hunt runtime init failed for '{contractId}': target group has no spawnable prototypes.");
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
                $"[Contracts] Hunt runtime init failed for '{contractId}': target group '{group.ID}' has no valid entity prototypes.");
            return false;
        }

        prototypeId = _random.Pick(candidates);
        return true;
    }

    private static bool IsSpawnedHuntTarget(ObjectiveRuntimeState state, EntityUid target)
    {
        for (var i = 0; i < state.HuntSpawnedTargets.Count; i++)
        {
            if (state.HuntSpawnedTargets[i] == target)
                return true;
        }

        return false;
    }

    private static void RemoveSpawnedHuntTarget(ObjectiveRuntimeState state, EntityUid target)
    {
        for (var i = state.HuntSpawnedTargets.Count - 1; i >= 0; i--)
        {
            if (state.HuntSpawnedTargets[i] == target)
                state.HuntSpawnedTargets.RemoveAt(i);
        }
    }

    private bool IsMatchingSpawnedHuntTarget(EntityUid entity, ContractServerData contract, bool allowDeadTarget)
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
            if (MatchesSpawnedHuntTargetEntry(prototypeId, targets[i]))
                return true;
        }

        return false;
    }

    private bool MatchesSpawnedHuntTargetEntry(string prototypeId, ContractTargetServerData target)
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
