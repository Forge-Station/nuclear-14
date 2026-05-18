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
        out ClaimAttemptResult fail)
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

            if (!TryAppendRetrievalSpawnedEntityTakePlanForRequirement(
                    store,
                    user,
                    crateEntity,
                    trackedCrateItems,
                    trackedUserItems,
                    trackedStoreNearbyItems,
                    target.TargetItem,
                    target.MatchMode,
                    target.Required,
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
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var need = required;
        need -= AppendRetrievalSpawnedEntityTakePlanFromSource(crateEntity, trackedCrateItems, targetItem, matchMode, need, takePlan, used);
        need -= AppendRetrievalSpawnedEntityTakePlanFromSource(user, trackedUserItems, targetItem, matchMode, need, takePlan, used);
        need -= AppendRetrievalSpawnedEntityTakePlanFromSource(store, trackedStoreNearbyItems, targetItem, matchMode, need, takePlan, used, worldTurnInSource: true);

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
        bool worldTurnInSource = false)
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
            takePlan.Add(new ClaimTakeEntry(source, ent, 1, false));
            taken++;
        }

        return taken;
    }

    private void RefreshRetrievalSpawnedProgressForClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid> storeNearbyItems)
    {
        if (_progressScratchInUse)
        {
            Sawmill.Warning(
                $"[Claim] Tracked Retrieval progress refresh for '{contractId}' on {ToPrettyString(store)} skipped because progress scratch is already in use. " +
                "Claim planning will still validate the current tracked item state.");
            return;
        }

        _progressScratchInUse = true;
        try
        {
            TryUpdateRetrievalSpawnedProgress(
                store,
                contractId,
                contract,
                user,
                _scratchUserItems,
                crateEntity,
                crateItems,
                storeNearbyItems,
                crateEntity != null && crateItems is { Count: > 0 });
        }
        finally
        {
            _progressScratchInUse = false;
        }
    }

    private bool TryUpdateRetrievalSpawnedProgress(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork)
    {
        if (!RequiresRetrievalSpawnedTurnIn(contract))
            return false;

        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
        {
            ResetContractProgress(contract);
            return true;
        }

        if (!TryGetRetrievalSpawnedRuntimeState(store, contractId, contract, out var state))
        {
            ResetContractProgress(contract);
            return true;
        }

        var trackedUserItems = FilterRetrievalSpawnedSourceItems(userItems, state);
        var trackedCrateItems = FilterRetrievalSpawnedSourceItems(crateItems, state);
        var trackedStoreNearbyItems = contract.AllowsStoreWorldTurnIn
            ? FilterRetrievalSpawnedSourceItems(storeNearbyItems, state)
            : new List<EntityUid>();
        var hasTrackedCrateWork = crate is { } && hasCrateWork && trackedCrateItems.Count > 0;

        UpdateRetrievalSpawnedEntityProgress(
            contract,
            store,
            user,
            trackedUserItems,
            crate,
            trackedCrateItems,
            trackedStoreNearbyItems,
            hasTrackedCrateWork);
        return true;
    }

    private void UpdateRetrievalSpawnedEntityProgress(
        ContractServerData contract,
        EntityUid store,
        EntityUid user,
        List<EntityUid> trackedUserItems,
        EntityUid? crate,
        List<EntityUid> trackedCrateItems,
        List<EntityUid> trackedStoreNearbyItems,
        bool hasTrackedCrateWork)
    {
        var targets = GetEffectiveTargets(contract);
        var totalRequired = CalculateTotalRequired(targets);
        var totalProgress = 0;
        var used = new HashSet<EntityUid>();

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
            {
                target.Progress = 0;
                continue;
            }

            var required = Math.Max(0, target.Required);
            var progress = CountRetrievalSpawnedEntitiesForRequirement(
                store,
                user,
                trackedUserItems,
                crate,
                trackedCrateItems,
                trackedStoreNearbyItems,
                hasTrackedCrateWork,
                target.TargetItem,
                target.MatchMode,
                required,
                used);

            target.Progress = Math.Min(required, progress);
            totalProgress = SaturatingAdd(totalProgress, target.Progress);
        }

        contract.Required = totalRequired;
        contract.Progress = Math.Min(totalRequired, totalProgress);
        if (targets.Count > 0)
            contract.TargetItem = targets[0].TargetItem;

        SyncContractFlowStatus(contract);
    }

    private int CountRetrievalSpawnedEntitiesForRequirement(
        EntityUid store,
        EntityUid user,
        List<EntityUid> trackedUserItems,
        EntityUid? crate,
        List<EntityUid> trackedCrateItems,
        List<EntityUid> trackedStoreNearbyItems,
        bool hasTrackedCrateWork,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required,
        HashSet<EntityUid> used)
    {
        var need = required;

        if (crate is { } crateRoot && hasTrackedCrateWork)
            need -= CountRetrievalSpawnedEntitiesFromSource(crateRoot, trackedCrateItems, targetItem, matchMode, need, used);

        need -= CountRetrievalSpawnedEntitiesFromSource(user, trackedUserItems, targetItem, matchMode, need, used);
        need -= CountRetrievalSpawnedEntitiesFromSource(store, trackedStoreNearbyItems, targetItem, matchMode, need, used, worldTurnInSource: true);

        return required - Math.Max(0, need);
    }

    private int CountRetrievalSpawnedEntitiesFromSource(
        EntityUid root,
        List<EntityUid>? items,
        string targetItem,
        PrototypeMatchMode matchMode,
        int need,
        HashSet<EntityUid> used,
        bool worldTurnInSource = false)
    {
        if (need <= 0 || items == null)
            return 0;

        var counted = 0;
        for (var i = 0; i < items.Count && counted < need; i++)
        {
            var ent = items[i];
            if (ent == EntityUid.Invalid || used.Contains(ent))
                continue;

            if (!CanUseContractPlanningEntity(root, ent, worldTurnInSource))
                continue;

            if (!MatchesRetrievalSpawnedEntityTarget(ent, targetItem, matchMode))
                continue;

            used.Add(ent);
            counted++;
        }

        return counted;
    }

    private bool MatchesRetrievalSpawnedEntityTarget(
        EntityUid ent,
        string targetItem,
        PrototypeMatchMode matchMode)
    {
        if (!TryGetPlanningEntityPrototypeId(ent, out var candidateId))
            return false;

        return MatchesPrototypeId(ent, candidateId, targetItem, matchMode);
    }

    private bool TryGetRetrievalSpawnedRuntimeState(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        out ObjectiveRuntimeState state)
    {
        state = default!;
        if (!RequiresRetrievalSpawnedTurnIn(contract))
            return false;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out state!))
            return false;

        PruneRetrievalSpawnedEntities(state);
        return state.RetrievalSpawnedEntities.Count > 0;
    }

    private void PruneRetrievalSpawnedEntities(ObjectiveRuntimeState state)
    {
        for (var i = state.RetrievalSpawnedEntities.Count - 1; i >= 0; i--)
        {
            var ent = state.RetrievalSpawnedEntities[i];
            if (ent == EntityUid.Invalid || TerminatingOrDeleted(ent))
                state.RetrievalSpawnedEntities.RemoveAt(i);
        }
    }

    private List<EntityUid> FilterRetrievalSpawnedSourceItems(
        IReadOnlyList<EntityUid>? sourceItems,
        ObjectiveRuntimeState state)
    {
        var filtered = new List<EntityUid>();
        if (sourceItems == null || sourceItems.Count == 0 || state.RetrievalSpawnedEntities.Count == 0)
            return filtered;

        for (var i = 0; i < sourceItems.Count; i++)
        {
            var ent = sourceItems[i];
            if (ent == EntityUid.Invalid)
                continue;

            if (IsRetrievalSpawnedEntity(ent, state))
                filtered.Add(ent);
        }

        return filtered;
    }

    private static bool IsRetrievalSpawnedEntity(EntityUid ent, ObjectiveRuntimeState state)
    {
        for (var i = 0; i < state.RetrievalSpawnedEntities.Count; i++)
        {
            if (state.RetrievalSpawnedEntities[i] == ent)
                return true;
        }

        return false;
    }
}
