using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryResolveSpawnedHuntPinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
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
        ObjectiveRuntimeState state
    )
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

            if (!_pinpointerService.TryGetOwner(_objectiveRuntime, pinpointer, out var owner) ||
                !TryResolveSpawnedHuntPinpointerTargetForUser(key.Store, owner, contract, state, out var target) ||
                target == EntityUid.Invalid ||
                TerminatingOrDeleted(target))
                continue;

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }

        return true;
    }

    private bool TryResolveSpawnedHuntPinpointerTarget(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
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
        out EntityUid target
    )
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

            if (!IsMatchingSpawnedHuntTarget(candidate, contract, false))
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
}
