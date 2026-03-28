using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void UpdateTrackedDeliveryDropoffObjectives()
    {
        if (_objectiveRuntimeByContract.Count == 0)
            return;

        _objectiveRuntimeKeysScratch.Clear();
        foreach (var (key, state) in _objectiveRuntimeByContract)
        {
            if (state.TargetEntity is not { } target ||
                target == EntityUid.Invalid ||
                TerminatingOrDeleted(target) ||
                state.DeliveryDropoffCoordinates == null)
            {
                continue;
            }

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                !contract.IsTrackedDeliveryObjective ||
                !UsesTrackedDeliveryDropoff(contract) ||
                contract.Completed)
            {
                continue;
            }

            if (IsTrackedDeliveryTargetAtDropoff(target, state))
                _objectiveRuntimeKeysScratch.Add(key);
        }

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
            CompleteTrackedDeliveryDropoffObjective(_objectiveRuntimeKeysScratch[i]);

        _objectiveRuntimeKeysScratch.Clear();
    }

    private void CompleteTrackedDeliveryDropoffObjective((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(target))
        {
            return;
        }

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, true);
            return;
        }

        if (!contract.Taken ||
            !contract.IsTrackedDeliveryObjective ||
            !UsesTrackedDeliveryDropoff(contract) ||
            contract.Completed)
        {
            return;
        }

        SetTrackedDeliveryProgress(contract, GetTrackedDeliveryAmount(contract, target));
        if (!contract.Completed)
            return;

        if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, Transform(target).Coordinates))
            return;

        state.DeliveryDropoffCompleted = true;

        var config = EnsureContractConfig(contract);
        if (!config.PreserveTargetOnComplete)
        {
            _objectiveRuntimeByTarget.Remove(target);
            state.TargetEntity = null;

            if (!TerminatingOrDeleted(target))
                Del(target);
        }

        DeactivateTrackedDeliveryDropoff(state);

        CleanupObjectivePinpointers(key, state);
    }

    private void HandleTrackedDeliveryTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract)
    {
        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);
    }

    private void UpdateTrackedDeliveryObjectiveProgress(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
        {
            SetTrackedDeliveryProgress(contract, 0);
            return;
        }

        if (TryUpdateTrackedDeliveryDropoffProgress(contract, state))
            return;

        UpdateTrackedDeliveryStoreProgress(store, contract, state, userItems, crateItems);
    }

    private bool TryUpdateTrackedDeliveryDropoffProgress(
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!UsesTrackedDeliveryDropoff(contract))
            return false;

        if (state.DeliveryDropoffCompleted)
        {
            SetTrackedDeliveryProgress(contract, GetTrackedDeliveryCompletionAmount(contract));
            return true;
        }

        var target = GetLiveTrackedDeliveryObjectiveTarget(state);
        var progress = target is { } deliveryTarget && IsTrackedDeliveryTargetAtDropoff(deliveryTarget, state)
            ? GetTrackedDeliveryAmount(contract, deliveryTarget)
            : 0;

        SetTrackedDeliveryProgress(contract, progress);
        return true;
    }

    private void UpdateTrackedDeliveryStoreProgress(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems)
    {
        var target = GetLiveTrackedDeliveryObjectiveTarget(state);
        if (target is not { } storeTarget)
        {
            SetTrackedDeliveryProgress(contract, 0);
            return;
        }

        var inUserInventory = ContainsTrackedDeliveryEntity(userItems, storeTarget);
        var inCrate = ContainsTrackedDeliveryEntity(crateItems, storeTarget);
        var atStore = IsTrackedDeliveryTargetAtStore(store, storeTarget);
        var progress = inUserInventory || inCrate || atStore
            ? GetTrackedDeliveryAmount(contract, storeTarget)
            : 0;

        SetTrackedDeliveryProgress(contract, progress);
    }

    private EntityUid? GetLiveTrackedDeliveryObjectiveTarget(ObjectiveRuntimeState state)
    {
        if (state.TargetEntity is not { } target || target == EntityUid.Invalid || TerminatingOrDeleted(target))
            return null;

        return target;
    }

    private ClaimAttemptResult TryClaimTrackedDeliveryContract(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return CreateTrackedDeliveryTargetLostResult();

        return UsesTrackedDeliveryDropoff(contract)
            ? TryClaimTrackedDeliveryDropoff(store, user, contractId, comp, contract, key, state)
            : TryClaimTrackedDeliveryStoreTarget(store, user, contractId, comp, contract, key, state);
    }

    private ClaimAttemptResult TryClaimTrackedDeliveryDropoff(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (state.DeliveryDropoffCompleted)
            SetTrackedDeliveryProgress(contract, GetTrackedDeliveryCompletionAmount(contract));

        if (!contract.Completed)
        {
            if (!TryGetLiveTrackedDeliveryTarget(state, out var target))
                return FailTrackedDeliveryObjective(key, comp, contract);

            var trackedAmount = IsTrackedDeliveryTargetAtDropoff(target, state)
                ? GetTrackedDeliveryAmount(contract, target)
                : 0;
            SetTrackedDeliveryProgress(contract, trackedAmount);
        }

        if (!contract.Completed)
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery target for '{contractId}' has not reached the dropoff point.");
        }

        return CompleteTrackedDeliveryClaim(store, user, contractId, comp, contract);
    }

    private ClaimAttemptResult TryClaimTrackedDeliveryStoreTarget(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (!TryGetLiveTrackedDeliveryTarget(state, out var target))
            return FailTrackedDeliveryObjective(key, comp, contract);

        ScanTrackedDeliveryTransferSources(user, out var crateEntity, out var crateItems);

        var inUserInventory = ContainsTrackedDeliveryEntity(_scratchUserItems, target);
        var inCrate = ContainsTrackedDeliveryEntity(crateItems, target);
        var atStore = IsTrackedDeliveryTargetAtStore(store, target);
        if (!inUserInventory && !inCrate && !atStore)
        {
            SetTrackedDeliveryProgress(contract, 0);
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery target for '{contractId}' is not present in user inventory, pulled crate or at the store.");
        }

        if (IsTrackedDeliveryProtectedFromDirectSale(user, target, crateEntity, inUserInventory, inCrate))
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery target for '{contractId}' is protected from direct sale.");
        }

        SetTrackedDeliveryProgress(contract, GetTrackedDeliveryAmount(contract, target));
        if (!contract.Completed)
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery progress {contract.Progress}/{contract.Required} for '{contractId}'.");
        }

        return CompleteTrackedDeliveryClaim(store, user, contractId, comp, contract);
    }

    private ClaimAttemptResult CreateTrackedDeliveryTargetLostResult()
    {
        return ClaimAttemptResult.Fail(
            ClaimFailureReason.ObjectiveFailed,
            Loc.GetString("nc-store-contract-delivery-target-lost"));
    }

    private ClaimAttemptResult FailTrackedDeliveryObjective(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);

        return CreateTrackedDeliveryTargetLostResult();
    }

    private ClaimAttemptResult CompleteTrackedDeliveryClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (!TryConsumeObjectiveProof(store, user, contractId, contract, out var proofFail))
            return proofFail;

        var config = EnsureContractConfig(contract);
        GiveContractRewards(user, contract.Rewards);
        FinalizeClaim(
            store,
            comp,
            contractId,
            contract.Repeatable,
            deleteTrackedEntities: !config.PreserveTargetOnComplete);
        return ClaimAttemptResult.Ok();
    }

    private bool TryGetLiveTrackedDeliveryTarget(ObjectiveRuntimeState state, out EntityUid target)
    {
        target = EntityUid.Invalid;

        if (state.TargetEntity is not { } existingTarget ||
            existingTarget == EntityUid.Invalid ||
            TerminatingOrDeleted(existingTarget))
        {
            return false;
        }

        target = existingTarget;
        return true;
    }

    private void ScanTrackedDeliveryTransferSources(
        EntityUid user,
        out EntityUid? crateEntity,
        out List<EntityUid>? crateItems
    )
    {
        _logic.ScanInventoryItems(user, _scratchUserItems);

        crateEntity = null;
        crateItems = null;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is not { } pulledCrate || !Exists(pulledCrate))
            return;

        crateEntity = pulledCrate;
        _logic.ScanInventoryItems(pulledCrate, _scratchCrateItems);
        crateItems = _scratchCrateItems;
    }

    private bool IsTrackedDeliveryProtectedFromDirectSale(
        EntityUid user,
        EntityUid target,
        EntityUid? crateEntity,
        bool inUserInventory,
        bool inCrate
    )
    {
        return (inUserInventory && _logic.IsProtectedFromDirectSale(user, target)) ||
               (inCrate && crateEntity is { } crate && _logic.IsProtectedFromDirectSale(crate, target));
    }

    private static bool UsesTrackedDeliveryDropoff(ContractServerData contract)
    {
        var config = EnsureContractConfig(contract);
        return !string.IsNullOrWhiteSpace(config.DropoffPointTag) ||
               config.DropoffPointTags is { Count: > 0 };
    }

    private bool IsTrackedDeliveryTargetAtDropoff(EntityUid target, ObjectiveRuntimeState state)
    {
        if (state.DeliveryDropoffCoordinates is not { } dropoff)
            return false;

        if (!TryComp(target, out TransformComponent? targetXform))
            return false;

        if (IsTargetInEntityContainer(targetXform))
            return false;

        var targetMap = _xform.ToMapCoordinates(targetXform.Coordinates);
        if (targetMap.MapId != dropoff.MapId)
            return false;

        var targetPos = _xform.GetWorldPosition(targetXform);
        var delta = targetPos - dropoff.Position;
        return delta.LengthSquared() <= NcContractTuning.TrackedDeliveryDropoffRange * NcContractTuning.TrackedDeliveryDropoffRange;
    }

    private bool IsTrackedDeliveryTargetAtStore(EntityUid store, EntityUid target)
    {
        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(target, out TransformComponent? targetXform))
        {
            return false;
        }

        if (IsTargetInEntityContainer(targetXform))
            return false;

        var storeMap = _xform.ToMapCoordinates(storeXform.Coordinates);
        var targetMap = _xform.ToMapCoordinates(targetXform.Coordinates);
        if (storeMap.MapId != targetMap.MapId)
            return false;

        var delta = targetMap.Position - storeMap.Position;
        return delta.LengthSquared() <= NcContractTuning.TrackedDeliveryStoreRange * NcContractTuning.TrackedDeliveryStoreRange;
    }

    private static bool ContainsTrackedDeliveryEntity(IReadOnlyList<EntityUid>? items, EntityUid target)
    {
        if (items == null)
            return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] == target)
                return true;
        }

        return false;
    }

    private int GetTrackedDeliveryAmount(ContractServerData contract, EntityUid target)
    {
        var required = Math.Max(1, contract.Required);

        if (TryComp(target, out StackComponent? stack))
            return Math.Clamp(stack.Count, 0, required);

        return Math.Min(required, 1);
    }

    private static int GetTrackedDeliveryCompletionAmount(ContractServerData contract)
    {
        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
            return Math.Max(1, contract.Required);

        var totalRequired = 0;
        for (var i = 0; i < targets.Count; i++)
            totalRequired = SaturatingAdd(totalRequired, Math.Max(0, targets[i].Required));

        return Math.Max(1, totalRequired);
    }

    private static void SetTrackedDeliveryProgress(ContractServerData contract, int trackedAmount)
    {
        var targets = GetEffectiveTargets(contract);
        if (targets.Count > 0)
        {
            var totalRequired = 0;
            var totalProgress = 0;
            var remaining = Math.Max(0, trackedAmount);

            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var required = Math.Max(0, target.Required);
                totalRequired = SaturatingAdd(totalRequired, required);

                var progress = Math.Min(required, remaining);
                target.Progress = progress;
                targets[i] = target;

                totalProgress = SaturatingAdd(totalProgress, progress);
                remaining = Math.Max(0, remaining - progress);
            }

            contract.Required = totalRequired;
            contract.Progress = Math.Min(totalRequired, totalProgress);
            contract.TargetItem = targets[0].TargetItem;
            SyncContractFlowStatus(contract);
            return;
        }

        var requiredTotal = Math.Max(1, contract.Required);
        contract.Required = requiredTotal;
        contract.Progress = Math.Clamp(trackedAmount, 0, requiredTotal);
        SyncContractFlowStatus(contract);
    }
}
