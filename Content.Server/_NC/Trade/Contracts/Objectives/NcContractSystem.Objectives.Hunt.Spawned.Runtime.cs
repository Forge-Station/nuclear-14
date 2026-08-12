using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TrySpawnHuntTargets(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
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

                if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out var spawnCoords, false))
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
        ObjectiveRuntimeState state
    )
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
            return false;

        if (!TryComp(candidate, out MobStateComponent? mobState) ||
            mobState.CurrentState != MobState.Dead)
            return false;

        body = candidate;
        return true;
    }

    private bool TryConsumeSpawnedHuntBodyTurnIn(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveConsumeJournal journal,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!RequiresSpawnedHuntBodyTurnIn(contract))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
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

        journal.TrackHuntBody(state, body);
        state.HuntBodyEntity = null;
        RemoveSpawnedHuntTarget(state, body);
        journal.PendingDeletes.Add(body);

        return true;
    }

    private bool IsSpawnedHuntBodyInTurnInScope(EntityUid store, EntityUid user, EntityUid body)
    {
        if (IsSpawnedHuntBodyCarriedByUser(body, user))
            return true;

        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(body, out TransformComponent? bodyXform) ||
            IsTargetInEntityContainer(bodyXform))
            return false;

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
        out string prototypeId
    )
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
}
