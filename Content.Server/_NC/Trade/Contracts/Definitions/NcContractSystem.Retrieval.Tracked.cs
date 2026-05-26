using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private static bool RequiresRetrievalSpawnedTurnIn(ContractServerData contract)
    {
        var config = contract.Config;
        return contract.IsInventoryDelivery &&
            config.RetrievalSpawnEnabled &&
            config.RetrievalRequireSpawnedEntities &&
            !RequiresRetrievalRouteDelivery(contract);
    }

    private bool TryPrepareRetrievalSpawnedClaimContext(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid> storeNearbyItems,
        out ClaimContext ctx,
        out ClaimAttemptResult fail
    )
    {
        ctx = default;
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryGetRetrievalSpawnedRuntimeState(store, contractId, contract, out var state))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.NotEnoughItems,
                $"Tracked Retrieval '{contractId}' has no live spawned target entities available for turn-in.");
            return false;
        }

        var trackedUserItems = FilterRetrievalSpawnedSourceItems(_scratchUserItems, state);
        var trackedCrateItems = FilterRetrievalSpawnedSourceItems(crateItems, state);
        var trackedStoreNearbyItems = FilterRetrievalSpawnedSourceItems(storeNearbyItems, state);

        ClearClaimPlanningScratch();
        var takePlan = new List<ClaimTakeEntry>(Math.Max(4, Math.Min(64, CalculateTotalRequired(targets))));
        var used = new HashSet<EntityUid>();
        var turnedInLeftByKey = new Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int>();

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
            {
                ClearClaimPlanningScratch();
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.InvalidTarget,
                    $"Invalid target '{target.TargetItem}' (required={target.Required}).");
                return false;
            }

            var remaining = GetRemainingTurnInRequirement(store, contractId, target, turnedInLeftByKey);
            if (remaining <= 0)
                continue;

            if (!TryAppendRetrievalSpawnedEntityTakePlanForRequirement(
                store,
                user,
                crateEntity,
                trackedCrateItems,
                trackedUserItems,
                trackedStoreNearbyItems,
                target.TargetItem,
                target.MatchMode,
                remaining,
                takePlan,
                used,
                out fail))
            {
                ClearClaimPlanningScratch();
                return false;
            }
        }

        ClearClaimPlanningScratch();
        ctx = CreateClaimContext(store, user, crateEntity, comp, contract, targets, crateItems, takePlan);
        return true;
    }

    private bool TryPreparePartialRetrievalSpawnedClaimContext(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid> storeNearbyItems,
        out ClaimContext ctx,
        out ClaimAttemptResult fail
    )
    {
        ctx = default;
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryGetRetrievalSpawnedRuntimeState(store, contractId, contract, out var state))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.NotEnoughItems,
                $"Tracked Retrieval '{contractId}' has no live spawned target entities available for partial turn-in.");
            return false;
        }

        var trackedUserItems = FilterRetrievalSpawnedSourceItems(_scratchUserItems, state);
        var trackedCrateItems = FilterRetrievalSpawnedSourceItems(crateItems, state);
        var trackedStoreNearbyItems = FilterRetrievalSpawnedSourceItems(storeNearbyItems, state);

        ClearClaimPlanningScratch();
        var takePlan = new List<ClaimTakeEntry>(Math.Max(4, Math.Min(64, targets.Count * 2)));
        var used = new HashSet<EntityUid>();
        var turnedInLeftByKey = new Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int>();

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
                continue;

            var remaining = GetRemainingTurnInRequirement(store, contractId, target, turnedInLeftByKey);
            if (remaining <= 0)
                continue;

            var need = remaining;
            need -= AppendRetrievalSpawnedEntityTakePlanFromSource(
                crateEntity,
                trackedCrateItems,
                target.TargetItem,
                target.MatchMode,
                need,
                takePlan,
                used);
            need -= AppendRetrievalSpawnedEntityTakePlanFromSource(
                user,
                trackedUserItems,
                target.TargetItem,
                target.MatchMode,
                need,
                takePlan,
                used);
            need -= AppendRetrievalSpawnedEntityTakePlanFromSource(
                store,
                trackedStoreNearbyItems,
                target.TargetItem,
                target.MatchMode,
                need,
                takePlan,
                used,
                true);
        }

        ClearClaimPlanningScratch();
        if (takePlan.Count == 0)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.NotEnoughItems,
                $"No tracked Retrieval cargo available for partial turn-in of '{contractId}'.");
            return false;
        }

        ctx = CreateClaimContext(store, user, crateEntity, comp, contract, targets, crateItems, takePlan);
        return true;
    }

    private bool TryAppendRetrievalSpawnedEntityTakePlanForRequirement(
        EntityUid store,
        EntityUid user,
        EntityUid? crateEntity,
        List<EntityUid>? trackedCrateItems,
        List<EntityUid> trackedUserItems,
        List<EntityUid> trackedStoreNearbyItems,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required,
        List<ClaimTakeEntry> takePlan,
        HashSet<EntityUid> used,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var need = required;
        need -= AppendRetrievalSpawnedEntityTakePlanFromSource(
            crateEntity,
            trackedCrateItems,
            targetItem,
            matchMode,
            need,
            takePlan,
            used);
        need -= AppendRetrievalSpawnedEntityTakePlanFromSource(
            user,
            trackedUserItems,
            targetItem,
            matchMode,
            need,
            takePlan,
            used);
        need -= AppendRetrievalSpawnedEntityTakePlanFromSource(
            store,
            trackedStoreNearbyItems,
            targetItem,
            matchMode,
            need,
            takePlan,
            used,
            true);

        if (need <= 0)
            return true;

        fail = ClaimAttemptResult.Fail(
            ClaimFailureReason.NotEnoughItems,
            $"need {required}x {targetItem} (mode={matchMode}) from spawned Retrieval entities, missing {need}.");
        return false;
    }

    private int AppendRetrievalSpawnedEntityTakePlanFromSource(
        EntityUid? root,
        List<EntityUid>? items,
        string targetItem,
        PrototypeMatchMode matchMode,
        int need,
        List<ClaimTakeEntry> takePlan,
        HashSet<EntityUid> used,
        bool worldTurnInSource = false
    )
    {
        if (need <= 0 || root is not { } source || items == null)
            return 0;

        var taken = 0;
        for (var i = 0; i < items.Count && taken < need; i++)
        {
            var ent = items[i];
            if (ent == EntityUid.Invalid || used.Contains(ent))
                continue;

            if (!CanUseContractPlanningEntity(source, ent, worldTurnInSource))
                continue;

            if (!MatchesRetrievalSpawnedEntityTarget(ent, targetItem, matchMode))
                continue;

            used.Add(ent);
            takePlan.Add(new(source, ent, 1, false, targetItem, matchMode));
            taken++;
        }

        return taken;
    }
}
