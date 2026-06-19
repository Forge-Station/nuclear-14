using Content.Shared._NC.Trade;
using Content.Shared.Stacks;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryGetLiveTrackedDeliveryTarget(ObjectiveRuntimeState state, out EntityUid target)
    {
        target = EntityUid.Invalid;

        if (state.TargetEntity is not { } existingTarget ||
            existingTarget == EntityUid.Invalid ||
            TerminatingOrDeleted(existingTarget))
            return false;

        target = existingTarget;
        return true;
    }

    private void ScanTrackedDeliveryTransferSources(
        EntityUid user,
        out List<EntityUid> userItems,
        out EntityUid? crateEntity,
        out List<EntityUid>? crateItems
    )
    {
        userItems = new(32);
        _logic.ScanInventoryItems(user, userItems);

        crateEntity = null;
        crateItems = null;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is not { } pulledCrate || !Exists(pulledCrate))
            return;

        crateEntity = pulledCrate;
        crateItems = new(32);
        _logic.ScanInventoryItems(pulledCrate, crateItems);
    }

    private bool IsTrackedDeliveryProtectedFromDirectSale(
        EntityUid user,
        EntityUid target,
        EntityUid? crateEntity,
        bool inUserInventory,
        bool inCrate
    ) =>
        inUserInventory && _logic.IsProtectedFromDirectSale(user, target) ||
        inCrate && crateEntity is { } crate && _logic.IsProtectedFromDirectSale(crate, target);

    private static bool UsesTrackedDeliveryDropoff(ContractServerData contract)
    {
        var config = contract.Config;
        return config.DropoffPoint != null;
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
        return delta.LengthSquared() <=
            NcContractTuning.TrackedDeliveryDropoffRange * NcContractTuning.TrackedDeliveryDropoffRange;
    }

    private bool IsTrackedDeliveryTargetAtStore(EntityUid store, EntityUid target)
    {
        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(target, out TransformComponent? targetXform))
            return false;

        if (IsTargetInEntityContainer(targetXform))
            return false;

        var storeMap = _xform.ToMapCoordinates(storeXform.Coordinates);
        var targetMap = _xform.ToMapCoordinates(targetXform.Coordinates);
        if (storeMap.MapId != targetMap.MapId)
            return false;

        var delta = targetMap.Position - storeMap.Position;
        return delta.LengthSquared() <=
            NcContractTuning.TrackedDeliveryStoreRange * NcContractTuning.TrackedDeliveryStoreRange;
    }

    private static bool ContainsTrackedDeliveryEntity(IReadOnlyList<EntityUid>? items, EntityUid target)
    {
        if (items == null)
            return false;

        for (var i = 0; i < items.Count; i++)
            if (items[i] == target)
                return true;

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
