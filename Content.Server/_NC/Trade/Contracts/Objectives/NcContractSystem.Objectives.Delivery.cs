using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
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
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(target))
        {
            SetTrackedDeliveryProgress(contract, 0);
            return;
        }

        var inUserInventory = ContainsTrackedDeliveryEntity(userItems, target);
        var inCrate = ContainsTrackedDeliveryEntity(crateItems, target);
        var progress = inUserInventory || inCrate
            ? GetTrackedDeliveryAmount(contract, target)
            : 0;

        SetTrackedDeliveryProgress(contract, progress);
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
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(target))
        {
            FinalizeObjectiveFailure(
                key,
                comp,
                contract,
                Loc.GetString("nc-store-contract-delivery-target-lost"),
                deleteGuards: false);

            return ClaimAttemptResult.Fail(ClaimFailureReason.ObjectiveFailed, Loc.GetString("nc-store-contract-delivery-target-lost"));
        }

        _logic.ScanInventoryItems(user, _scratchUserItems);

        EntityUid? crateEntity = null;
        List<EntityUid>? crateItems = null;
        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is { } pulledCrate && Exists(pulledCrate))
        {
            crateEntity = pulledCrate;
            _logic.ScanInventoryItems(pulledCrate, _scratchCrateItems);
            crateItems = _scratchCrateItems;
        }

        var inUserInventory = ContainsTrackedDeliveryEntity(_scratchUserItems, target);
        var inCrate = ContainsTrackedDeliveryEntity(crateItems, target);
        if (!inUserInventory && !inCrate)
        {
            SetTrackedDeliveryProgress(contract, 0);
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery target for '{contractId}' is not present in user inventory or pulled crate.");
        }

        if ((inUserInventory && _logic.IsProtectedFromDirectSale(user, target)) ||
            (inCrate && crateEntity is { } crate && _logic.IsProtectedFromDirectSale(crate, target)))
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

        GiveContractRewards(user, contract.Rewards);
        FinalizeClaim(store, comp, contractId, contract.Repeatable);
        return ClaimAttemptResult.Ok();
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
