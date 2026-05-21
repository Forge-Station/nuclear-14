using Content.Shared._NC.Trade;
using Content.Shared.Stacks;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void UpdateSingleTargetContractProgress(
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        int targetIndex,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork
    )
    {
        var target = targets[targetIndex];
        contract.TargetItem = target.TargetItem;

        if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
        {
            target.Progress = 0;
            targets[targetIndex] = target;
            contract.Required = 0;
            contract.Progress = 0;
            SyncContractFlowStatus(contract);
            return;
        }

        var required = Math.Max(0, target.Required);
        var progressed = ComputeProgressForTarget(
            store,
            user,
            userItems,
            crate,
            crateItems,
            storeNearbyItems,
            hasCrateWork,
            target.TargetItem,
            target.MatchMode,
            required);

        target.Progress = progressed;
        targets[targetIndex] = target;
        contract.Required = required;
        contract.Progress = progressed;
        SyncContractFlowStatus(contract);
    }

    private int ComputeProgressForTarget(
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required
    )
    {
        if (string.IsNullOrWhiteSpace(targetItem) || required <= 0)
            return 0;

        var progressed = ReserveProgressAcrossSources(
            store,
            user,
            userItems,
            crate,
            crateItems,
            storeNearbyItems,
            hasCrateWork,
            targetItem,
            matchMode,
            required);

        return Math.Clamp(progressed, 0, required);
    }

    private int ReserveProgressAcrossSources(
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required
    )
    {
        var need = required;

        if (crate is { } crateRoot && hasCrateWork)
            need -= ReserveProgressFromSource(crateRoot, crateItems, targetItem, matchMode, need);

        need -= ReserveProgressFromSource(user, userItems, targetItem, matchMode, need);
        need -= ReserveProgressFromSource(store, storeNearbyItems, targetItem, matchMode, need, true);

        var progressed = required - Math.Max(0, need);
        return Math.Max(0, progressed);
    }

    private int ReserveProgressFromSource(
        EntityUid root,
        IReadOnlyList<EntityUid>? items,
        string targetItem,
        PrototypeMatchMode matchMode,
        int need,
        bool worldTurnInSource = false
    )
    {
        if (need <= 0 || items == null)
            return 0;

        return ReserveProgressFromItems(
            root,
            items,
            targetItem,
            matchMode,
            need,
            _progressVirtualStackLeftScratch,
            _progressConsumedEntitiesScratch,
            worldTurnInSource);
    }

    private void ClearProgressPerContractScratch()
    {
        if (_progressTargetIndexesByKeyScratch.Count > 0)
        {
            foreach (var indexes in _progressTargetIndexesByKeyScratch.Values)
            {
                indexes.Clear();
                _progressTargetIndexPool.Push(indexes);
            }

            _progressTargetIndexesByKeyScratch.Clear();
        }

        _progressRequiredByKeyScratch.Clear();
        _progressClaimableByKeyScratch.Clear();
        ClearProgressReservationScratch();
        _progressOrderedKeysScratch.Clear();
    }

    private void ClearProgressReservationScratch()
    {
        _progressVirtualStackLeftScratch.Clear();
        _progressConsumedEntitiesScratch.Clear();
    }

    private List<int> RentProgressTargetIndexList()
    {
        if (_progressTargetIndexPool.Count > 0)
            return _progressTargetIndexPool.Pop();

        return new(4);
    }

    private int ReserveProgressFromItems(
        EntityUid root,
        IReadOnlyList<EntityUid> items,
        string expectedProtoId,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        HashSet<EntityUid> consumedNonStack,
        bool worldTurnInSource = false
    )
    {
        if (need <= 0)
            return 0;

        return TryGetStackTypeId(expectedProtoId, out var stackTypeId)
            ? ReserveProgressFromStackItems(root, items, stackTypeId, need, virtualStackLeft, worldTurnInSource)
            : ReserveProgressFromPrototypeItems(
                root,
                items,
                expectedProtoId,
                matchMode,
                need,
                virtualStackLeft,
                consumedNonStack,
                worldTurnInSource);
    }

    private int ReserveProgressFromStackItems(
        EntityUid root,
        IReadOnlyList<EntityUid> items,
        string stackTypeId,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
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

            reserved += ReserveAvailableStackAmount(ent, need - reserved, virtualStackLeft, out _);
        }

        return reserved;
    }

    private int ReserveProgressFromPrototypeItems(
        EntityUid root,
        IReadOnlyList<EntityUid> items,
        string expectedProtoId,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        HashSet<EntityUid> consumedNonStack,
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
                continue;

            if (TryComp(ent, out StackComponent? _))
            {
                reserved += ReserveAvailableStackAmount(ent, need - reserved, virtualStackLeft, out _);
                continue;
            }

            if (consumedNonStack.Add(ent))
                reserved += 1;
        }

        return reserved;
    }
}
