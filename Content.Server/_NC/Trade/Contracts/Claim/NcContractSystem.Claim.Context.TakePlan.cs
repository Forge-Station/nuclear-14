using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryAppendTakePlanForRequirement(
        EntityUid store,
        EntityUid user,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid>? storeNearbyItems,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required,
        List<ClaimTakeEntry> takePlan,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var need = required;
        need -= AppendTakePlanFromSource(crateEntity, crateItems, targetItem, matchMode, need, takePlan);
        need -= AppendTakePlanFromSource(user, _scratchUserItems, targetItem, matchMode, need, takePlan);
        need -= AppendTakePlanFromSource(store, storeNearbyItems, targetItem, matchMode, need, takePlan, worldTurnInSource: true);

        if (need <= 0)
            return true;

        fail = CreateClaimPlanningFailure(crateEntity, storeNearbyItems, targetItem, matchMode, required, need);
        return false;
    }

    private int AppendTakePlanFromSource(
        EntityUid? root,
        List<EntityUid>? items,
        string targetItem,
        PrototypeMatchMode matchMode,
        int need,
        List<ClaimTakeEntry> takePlan,
        bool worldTurnInSource = false
    )
    {
        if (need <= 0 || root is not { } source || items == null)
            return 0;

        return ReserveTakePlanFromItems(
            source,
            items,
            targetItem,
            matchMode,
            need,
            _claimVirtualStackLeftScratch,
            takePlan,
            worldTurnInSource);
    }

    private static ClaimAttemptResult CreateClaimPlanningFailure(
        EntityUid? crateEntity,
        List<EntityUid>? storeNearbyItems,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required,
        int missing
    )
    {
        if (crateEntity == null && (storeNearbyItems == null || storeNearbyItems.Count == 0))
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingCrate,
                $"need {required}x {targetItem} (mode={matchMode}), missing {missing}. Pull a closed crate to claim from it.");
        }

        return ClaimAttemptResult.Fail(
            ClaimFailureReason.NotEnoughItems,
            $"need {required}x {targetItem} (mode={matchMode}), missing {missing} after planning.");
    }

    private int ReserveTakePlanFromItems(
        EntityUid root,
        List<EntityUid> items,
        string expectedProtoId,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
        bool worldTurnInSource = false
    )
    {
        if (need <= 0)
            return 0;

        return TryGetStackTypeId(expectedProtoId, out var stackTypeId)
            ? ReserveTakePlanFromStackItems(
                root,
                items,
                expectedProtoId,
                matchMode,
                stackTypeId,
                need,
                virtualStackLeft,
                planOut,
                worldTurnInSource)
            : ReserveTakePlanFromPrototypeItems(
                root,
                items,
                expectedProtoId,
                matchMode,
                need,
                virtualStackLeft,
                planOut,
                worldTurnInSource);
    }

    private int ReserveTakePlanFromStackItems(
        EntityUid root,
        List<EntityUid> items,
        string targetItem,
        PrototypeMatchMode matchMode,
        string stackTypeId,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
        bool worldTurnInSource
    )
    {
        var reserved = 0;

        for (var i = 0; i < items.Count && reserved < need; i++)
        {
            var ent = items[i];
            if (!CanUseContractPlanningEntity(root, ent, worldTurnInSource))
                continue;

            if (!TryComp(ent, out StackComponent? stack) || stack.StackTypeId != stackTypeId)
                continue;

            reserved += AppendClaimStackTake(
                root,
                ent,
                targetItem,
                matchMode,
                need - reserved,
                virtualStackLeft,
                planOut,
                items,
                i);
        }

        return reserved;
    }

    private int ReserveTakePlanFromPrototypeItems(
        EntityUid root,
        List<EntityUid> items,
        string expectedProtoId,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
        bool worldTurnInSource
    )
    {
        var reserved = 0;

        for (var i = 0; i < items.Count && reserved < need; i++)
        {
            var ent = items[i];
            if (!CanUseContractPlanningEntity(root, ent, worldTurnInSource))
                continue;

            if (!TryGetPlanningEntityPrototypeId(ent, out var candidateId) ||
                !MatchesPrototypeId(ent, candidateId, expectedProtoId, matchMode))
            {
                continue;
            }

            if (TryComp(ent, out StackComponent? _))
            {
                reserved += AppendClaimStackTake(
                    root,
                    ent,
                    expectedProtoId,
                    matchMode,
                    need - reserved,
                    virtualStackLeft,
                    planOut,
                    items,
                    i);
                continue;
            }

            reserved += AppendClaimEntityTake(root, ent, expectedProtoId, matchMode, planOut, items, i);
        }

        return reserved;
    }

    private int AppendClaimStackTake(
        EntityUid root,
        EntityUid ent,
        string targetItem,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
        List<EntityUid> items,
        int index
    )
    {
        var take = ReserveAvailableStackAmount(ent, need, virtualStackLeft, out var exhausted);
        if (exhausted)
            items[index] = EntityUid.Invalid;

        if (take <= 0)
            return 0;

        planOut.Add(new ClaimTakeEntry(root, ent, take, true, targetItem, matchMode));
        return take;
    }

    private static int AppendClaimEntityTake(
        EntityUid root,
        EntityUid ent,
        string targetItem,
        PrototypeMatchMode matchMode,
        List<ClaimTakeEntry> planOut,
        List<EntityUid> items,
        int index
    )
    {
        planOut.Add(new ClaimTakeEntry(root, ent, 1, false, targetItem, matchMode));
        items[index] = EntityUid.Invalid;
        return 1;
    }
}
