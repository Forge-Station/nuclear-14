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
        if (TryFailRetrievalSpawnedTurnInIfTrackedCargoWasLost(key, contract, state))
            return false;

        return state.RetrievalSpawnedEntities.Count > 0;
    }

    private void RegisterRetrievalSpawnedCargo(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid cargo)
    {
        if (state.RetrievalSpawnedEntitySet.Add(cargo))
            state.RetrievalSpawnedEntities.Add(cargo);

        _objectiveRuntimeByRetrievalCargo[cargo] = key;
    }

    private void UnregisterRetrievalSpawnedCargo(EntityUid cargo)
    {
        if (cargo == EntityUid.Invalid)
            return;

        _objectiveRuntimeByRetrievalCargo.Remove(cargo);
    }

    private void UnregisterRetrievalSpawnedCargoTakePlan(
        ContractServerData contract,
        List<ClaimTakeEntry> takePlan)
    {
        if (!RequiresRetrievalSpawnedTurnIn(contract))
            return;

        for (var i = 0; i < takePlan.Count; i++)
            UnregisterRetrievalSpawnedCargo(takePlan[i].Entity);
    }

    private void RemoveRetrievalSpawnedCargoFromState(ObjectiveRuntimeState state, EntityUid cargo)
    {
        state.RetrievalSpawnedEntities.Remove(cargo);
        state.RetrievalSpawnedEntitySet.Remove(cargo);
        state.RetrievalDeliveredEntities.Remove(cargo);
    }

    private void OnRetrievalSpawnedCargoDestroyed(
        (EntityUid Store, string ContractId) key,
        EntityUid cargo)
    {
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        RemoveRetrievalSpawnedCargoFromState(state, cargo);

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken || contract.Runtime.Failed)
            return;

        if (!RequiresRetrievalSpawnedTurnIn(contract) &&
            !RequiresRetrievalRouteDelivery(contract))
        {
            return;
        }

        if (state.ProofSpawned || state.RetrievalRouteDeliveryCompleted)
            return;

        Sawmill.Warning(
            $"[Contracts] Retrieval cargo for '{key.ContractId}' was destroyed before turn-in on {ToPrettyString(key.Store)}; contract failed.");

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);
    }

    private bool TryFailRetrievalSpawnedTurnInIfTrackedCargoWasLost(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!RequiresRetrievalSpawnedTurnIn(contract) ||
            !contract.Taken ||
            contract.Runtime.Failed)
        {
            return false;
        }

        var required = CalculateTotalRequired(GetEffectiveTargets(contract));
        if (required <= 0 || state.RetrievalSpawnedEntities.Count >= required)
            return false;

        if (!TryGetObjectiveContract(key, out var comp, out _))
            return false;

        Sawmill.Warning(
            $"[Contracts] Retrieval cargo for '{key.ContractId}' is no longer available " +
            $"({state.RetrievalSpawnedEntities.Count}/{required} remaining). Contract failed.");

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);
        return true;
    }

    private void PruneRetrievalSpawnedEntities(ObjectiveRuntimeState state)
    {
        for (var i = state.RetrievalSpawnedEntities.Count - 1; i >= 0; i--)
        {
            var ent = state.RetrievalSpawnedEntities[i];
            if (ent == EntityUid.Invalid || TerminatingOrDeleted(ent))
            {
                UnregisterRetrievalSpawnedCargo(ent);
                state.RetrievalSpawnedEntities.RemoveAt(i);
                state.RetrievalSpawnedEntitySet.Remove(ent);
            }
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
        return state.RetrievalSpawnedEntitySet.Contains(ent);
    }
}
