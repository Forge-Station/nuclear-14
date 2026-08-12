using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryPrepareSingleTargetClaimContext(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid>? storeNearbyItems,
        out ClaimContext ctx,
        out ClaimAttemptResult fail
    )
    {
        ctx = default;
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var target = targets[0];
        if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.InvalidTarget,
                $"Invalid target '{target.TargetItem}' (required={target.Required}).");
            return false;
        }

        ClearClaimPlanningScratch();
        var remaining = GetRemainingTurnInRequirement(store, contractId, target);
        var takePlan = new List<ClaimTakeEntry>(Math.Max(4, Math.Min(32, Math.Max(remaining, 1))));

        if (remaining <= 0)
        {
            ClearClaimPlanningScratch();
            ctx = CreateClaimContext(store, user, crateEntity, comp, contract, targets, crateItems, takePlan);
            return true;
        }

        if (!TryAppendTakePlanForRequirement(
            store,
            user,
            crateEntity,
            crateItems,
            storeNearbyItems,
            target.TargetItem,
            target.MatchMode,
            remaining,
            takePlan,
            out fail))
        {
            ClearClaimPlanningScratch();
            return false;
        }

        ClearClaimPlanningScratch();
        ctx = CreateClaimContext(store, user, crateEntity, comp, contract, targets, crateItems, takePlan);
        return true;
    }

    private bool TryPreparePartialClaimContext(
        EntityUid store,
        EntityUid user,
        string contractId,
        out ClaimContext ctx,
        out ClaimAttemptResult fail
    )
    {
        ctx = default;
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryResolveClaimContract(store, contractId, out var comp, out var contract, out var targets, out fail))
            return false;

        PrepareClaimSources(store, user, contract, out var crateEntity, out var crateItems, out var storeNearbyItems);

        if (RequiresRetrievalSpawnedTurnIn(contract))
        {
            RefreshRetrievalSpawnedProgressForClaim(
                store,
                user,
                contractId,
                contract,
                crateEntity,
                crateItems,
                storeNearbyItems);

            return TryPreparePartialRetrievalSpawnedClaimContext(
                store,
                user,
                contractId,
                comp,
                contract,
                targets,
                crateEntity,
                crateItems,
                storeNearbyItems,
                out ctx,
                out fail);
        }

        RefreshInventoryDeliveryProgressForClaim(
            store,
            user,
            contractId,
            contract,
            crateEntity,
            crateItems,
            storeNearbyItems);

        return TryPreparePartialInventoryClaimContext(
            store,
            user,
            contractId,
            comp,
            contract,
            targets,
            crateEntity,
            crateItems,
            storeNearbyItems,
            out ctx,
            out fail);
    }

    private bool TryPreparePartialInventoryClaimContext(
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

        ClearClaimPlanningScratch();
        var takePlan = new List<ClaimTakeEntry>(Math.Max(4, Math.Min(64, targets.Count * 2)));
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
            need -= AppendTakePlanFromSource(
                crateEntity,
                crateItems,
                target.TargetItem,
                target.MatchMode,
                need,
                takePlan);
            need -= AppendTakePlanFromSource(
                user,
                _scratchUserItems,
                target.TargetItem,
                target.MatchMode,
                need,
                takePlan);
            need -= AppendTakePlanFromSource(
                store,
                storeNearbyItems,
                target.TargetItem,
                target.MatchMode,
                need,
                takePlan,
                true);
        }

        ClearClaimPlanningScratch();
        if (takePlan.Count == 0)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.NotEnoughItems,
                $"No partial turn-in items available for '{contractId}'.");
            return false;
        }

        ctx = CreateClaimContext(store, user, crateEntity, comp, contract, targets, crateItems, takePlan);
        return true;
    }
}
