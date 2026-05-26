using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private static bool IsSpawnedHuntTarget(ObjectiveRuntimeState state, EntityUid target)
    {
        for (var i = 0; i < state.HuntSpawnedTargets.Count; i++)
            if (state.HuntSpawnedTargets[i] == target)
                return true;

        return false;
    }

    private static void RemoveSpawnedHuntTarget(ObjectiveRuntimeState state, EntityUid target)
    {
        for (var i = state.HuntSpawnedTargets.Count - 1; i >= 0; i--)
            if (state.HuntSpawnedTargets[i] == target)
                state.HuntSpawnedTargets.RemoveAt(i);
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
            if (MatchesSpawnedHuntTargetEntry(prototypeId, targets[i]))
                return true;

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
            if (group.Prototypes[i] == prototypeId)
                return true;

        return false;
    }
}
