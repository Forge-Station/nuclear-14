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
        if (!IsHuntV2Contract(contract))
            return TryInitializeHuntObjective(store, user, contractId, contract);

        return TryInitializeHuntV2Objective(store, user, contractId, contract);
    }

    private static bool IsHuntV2Contract(ContractServerData contract)
    {
        return contract.IsHuntObjective && contract.Config.HuntV2Enabled;
    }

    private static bool RequiresHuntV2BodyTurnIn(ContractServerData contract)
    {
        return IsHuntV2Contract(contract) &&
               contract.Config.HuntV2CompletionMode == NcHuntCompletionMode.BodyTurnIn;
    }

    private bool TryInitializeHuntV2Objective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract)
    {
        if (contract.Config.HuntV2CompletionMode is not (NcHuntCompletionMode.TrophyTurnIn or NcHuntCompletionMode.BodyTurnIn))
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt runtime init failed for '{contractId}': only TrophyTurnIn and BodyTurnIn are supported.");
            return false;
        }

        if (contract.Config.HuntV2CompletionMode == NcHuntCompletionMode.TrophyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.ProofPrototype))
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt runtime init failed for '{contractId}': TrophyTurnIn requires proof prototype.");
            return false;
        }

        if (contract.Config.HuntV2CompletionMode == NcHuntCompletionMode.BodyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.HuntV2BodyPrototype))
        {
            Sawmill.Warning(
                $"[ContractsV2] Hunt runtime init failed for '{contractId}': BodyTurnIn requires a body target.");
            return false;
        }

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = null;
        state.HuntV2BodyEntity = null;
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

        if (!TryResolveHuntV2PinpointerTargetForUser(store, user, contract, state, out var pinpointerTarget))
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

            TryAdvanceHuntV2TargetProgress(killedTarget, contract, state);
            SetObjectiveStage(contract, CalculateHuntV2TotalProgress(contract));
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

            if (contract.Config.HuntV2CompletionMode == NcHuntCompletionMode.TrophyTurnIn)
            {
                var completionCoords = ResolveHuntObjectiveCompletionCoordinates(key.Store, state);
                if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, completionCoords))
                    continue;
            }
            else if (!TryGetHuntV2BodyEntity(state, out _))
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

            if (TryRetargetHuntV2CompletedPinpointersForOwners(key, contract, state))
                continue;

            if (TryResolveHuntV2PinpointerTarget(key.Store, contract, state, out var target))
                RetargetObjectivePinpointers(key, state, target);
        }
    }

    private void TryHandleHuntV2BodyEntityTerminating(EntityUid body)
    {
        if (body == EntityUid.Invalid || _objectiveRuntimeByContract.Count == 0)
            return;

        List<(EntityUid Store, string ContractId)>? candidates = null;
        foreach (var (key, state) in _objectiveRuntimeByContract)
        {
            if (!state.HuntV2Active || state.HuntV2BodyEntity != body)
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
                state.HuntV2BodyEntity != body)
            {
                continue;
            }

            state.HuntV2BodyEntity = null;
            RemoveHuntV2SpawnedTarget(state, body);

            if (!TryGetObjectiveContract(key, out var comp, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed ||
                (contract.Completed && !RequiresHuntV2BodyTurnIn(contract)))
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

    private bool TryResolveHuntV2PinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!IsHuntV2Contract(contract))
            return false;

        if (!contract.Completed)
            return TryResolveHuntV2PinpointerTarget(store, contract, state, out target);

        if (contract.Config.HuntV2CompletionMode == NcHuntCompletionMode.BodyTurnIn &&
            TryGetHuntV2BodyEntity(state, out var body))
        {
            target = IsHuntV2BodyCarriedByUser(body, user) ? store : body;
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

    private bool TryRetargetHuntV2CompletedPinpointersForOwners(
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
                !TryResolveHuntV2PinpointerTargetForUser(key.Store, owner, contract, state, out var target) ||
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
            if (contract.Config.HuntV2CompletionMode == NcHuntCompletionMode.BodyTurnIn &&
                TryGetHuntV2BodyEntity(state, out var body))
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
                if (targetDef.BodyRequired)
                    state.HuntV2BodyEntity = target;

                if (state.LastKnownTargetCoordinates == null && TryComp(target, out TransformComponent? targetXform))
                    state.LastKnownTargetCoordinates = targetXform.Coordinates;
            }
        }

        return state.HuntV2SpawnedTargets.Count == required;
    }

    private bool TryAdvanceHuntV2TargetProgress(
        EntityUid killedTarget,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!TryGetPlanningEntityPrototypeId(killedTarget, out var prototypeId))
            return false;

        var targets = GetEffectiveTargets(contract);
        if (state.HuntV2BodyEntity == killedTarget)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (!target.BodyRequired || target.Progress >= target.Required)
                    continue;

                if (!MatchesHuntV2TargetEntry(prototypeId, target))
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

            if (!MatchesHuntV2TargetEntry(prototypeId, target))
                continue;

            target.Progress = Math.Min(target.Required, target.Progress + 1);
            targets[i] = target;
            return true;
        }

        return false;
    }

    private static int CalculateHuntV2TotalProgress(ContractServerData contract)
    {
        var progress = 0;
        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
            progress = SaturatingAdd(progress, Math.Max(0, targets[i].Progress));

        return progress;
    }

    private bool TryGetHuntV2BodyEntity(ObjectiveRuntimeState state, out EntityUid body)
    {
        body = EntityUid.Invalid;
        if (state.HuntV2BodyEntity is not { } candidate ||
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

    private bool TryConsumeHuntV2BodyTurnIn(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!RequiresHuntV2BodyTurnIn(contract))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            !TryGetHuntV2BodyEntity(state, out var body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' requires the marked corpse to be brought back to the store.");
            return false;
        }

        if (!IsHuntV2BodyInTurnInScope(store, user, body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' body is not being dragged by the claimant and is not near the store.");
            return false;
        }

        state.HuntV2BodyEntity = null;
        RemoveHuntV2SpawnedTarget(state, body);
        if (EntityManager.EntityExists(body))
            Del(body);

        return true;
    }

    private bool IsHuntV2BodyInTurnInScope(EntityUid store, EntityUid user, EntityUid body)
    {
        if (IsHuntV2BodyCarriedByUser(body, user))
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

    private bool IsHuntV2BodyCarriedByUser(EntityUid body, EntityUid user)
    {
        if (TryComp(body, out PullableComponent? pullable) && pullable.Puller == user)
            return true;

        return TryGetContainedEntityRoot(body, out var root) && root == user;
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
