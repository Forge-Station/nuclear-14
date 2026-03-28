using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public void UpdateContractsProgress(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool includeStoreWorldItems
    )
    {
        if (comp.Contracts.Count == 0)
            return;

        var storeNearbyItemsPrepared = false;
        var hasCrateWork = crate is { } && crateItems is { Count: > 0 };
        PopulateProgressContractIds(comp);

        try
        {
            for (var i = 0; i < _progressContractIdsScratch.Count; i++)
                UpdateProgressForContract(
                    comp,
                    _progressContractIdsScratch[i],
                    store,
                    user,
                    userItems,
                    crate,
                    crateItems,
                    includeStoreWorldItems,
                    hasCrateWork,
                    ref storeNearbyItemsPrepared);
        }
        finally
        {
            _progressContractIdsScratch.Clear();
        }
    }

    private void PopulateProgressContractIds(NcStoreComponent comp)
    {
        _progressContractIdsScratch.Clear();
        foreach (var contractId in comp.Contracts.Keys)
            _progressContractIdsScratch.Add(contractId);
    }

    private bool TryGetProgressContract(
        NcStoreComponent comp,
        string contractId,
        out ContractServerData contract)
    {
        if (!comp.Contracts.TryGetValue(contractId, out contract!))
            return false;

        if (contract.Taken)
            return true;

        ResetContractProgress(contract);
        return false;
    }

    private void UpdateProgressForContract(
        NcStoreComponent comp,
        string contractId,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool includeStoreWorldItems,
        bool hasCrateWork,
        ref bool storeNearbyItemsPrepared)
    {
        if (!TryGetProgressContract(comp, contractId, out var contract))
            return;

        if (TryUpdateContractProgressByExecutionKind(store, contractId, contract, userItems, crateItems))
            return;

        EnsureStoreNearbyProgressItems(
            store,
            contract,
            includeStoreWorldItems,
            ref storeNearbyItemsPrepared);

        UpdateContractProgressForSingleContract(
            contract,
            store,
            user,
            userItems,
            crate,
            crateItems,
            _scratchStoreNearbyItems,
            hasCrateWork);
    }

    private bool TryUpdateContractProgressByExecutionKind(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems)
    {
        switch (contract.ExecutionKind)
        {
            case ContractExecutionKind.TrackedDeliveryObjective:
                UpdateTrackedDeliveryObjectiveProgress(store, contractId, contract, userItems, crateItems);
                return true;

            case ContractExecutionKind.HuntObjective:
            case ContractExecutionKind.RepairObjective:
            case ContractExecutionKind.GhostRoleObjective:
                UpdateObjectiveContractProgress(store, contractId, contract);
                return true;

            default:
                return false;
        }
    }

    private void EnsureStoreNearbyProgressItems(
        EntityUid store,
        ContractServerData contract,
        bool includeStoreWorldItems,
        ref bool storeNearbyItemsPrepared)
    {
        if (!includeStoreWorldItems || !contract.AllowsStoreWorldTurnIn || storeNearbyItemsPrepared)
            return;

        ScanStoreNearbyTurnInItems(store, _scratchStoreNearbyItems);
        storeNearbyItemsPrepared = true;
    }

    private static void ResetContractProgress(ContractServerData contract)
    {
        contract.Progress = 0;

        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            target.Progress = 0;
            targets[i] = target;
        }

        SyncContractFlowStatus(contract);
    }

    private void UpdateContractProgressForSingleContract(
        ContractServerData contract,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork
    )
    {
        var targets = GetEffectiveTargets(contract);
        if (TryUpdateSimpleContractProgress(
                contract,
                targets,
                store,
                user,
                userItems,
                crate,
                crateItems,
                storeNearbyItems,
                hasCrateWork))
        {
            return;
        }

        ClearProgressPerContractScratch();
        var totalRequired = CollectProgressRequirements(targets);
        if (_progressRequiredByKeyScratch.Count == 0)
        {
            ResetGroupedContractProgress(contract, targets);
            return;
        }

        SeedEmptyProgressClaims();
        ReserveGroupedContractProgress(
            store,
            user,
            userItems,
            crate,
            crateItems,
            storeNearbyItems,
            hasCrateWork);
        ApplyGroupedProgress(contract, targets, totalRequired);
    }

    private bool TryUpdateSimpleContractProgress(
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork
    )
    {
        if (targets.Count == 0)
        {
            ClearProgressReservationScratch();
            UpdateLegacyContractProgress(contract, store, user, userItems, crate, crateItems, storeNearbyItems, hasCrateWork);
            return true;
        }

        if (targets.Count != 1)
            return false;

        ClearProgressReservationScratch();
        UpdateSingleTargetContractProgress(contract, targets[0], store, user, userItems, crate, crateItems, storeNearbyItems, hasCrateWork);
        return true;
    }

    private int CollectProgressRequirements(List<ContractTargetServerData> targets)
    {
        var totalRequired = 0;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
            {
                target.Progress = 0;
                targets[i] = target;
                continue;
            }

            var key = (target.TargetItem, target.MatchMode);
            _progressRequiredByKeyScratch[key] = SaturatingAdd(_progressRequiredByKeyScratch.GetValueOrDefault(key, 0), target.Required);

            if (!_progressTargetIndexesByKeyScratch.TryGetValue(key, out var indexes))
            {
                indexes = RentProgressTargetIndexList();
                _progressTargetIndexesByKeyScratch[key] = indexes;
            }

            indexes.Add(i);
            totalRequired = SaturatingAdd(totalRequired, target.Required);
        }

        return totalRequired;
    }

    private void ResetGroupedContractProgress(ContractServerData contract, List<ContractTargetServerData> targets)
    {
        contract.Required = 0;
        contract.Progress = 0;

        if (targets.Count > 0)
            contract.TargetItem = targets[0].TargetItem;

        SyncContractFlowStatus(contract);
    }

    private void SeedEmptyProgressClaims()
    {
        foreach (var (key, required) in _progressRequiredByKeyScratch)
        {
            if (required <= 0)
                _progressClaimableByKeyScratch[key] = 0;
        }
    }

    private void ReserveGroupedContractProgress(
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork
    )
    {
        BuildOrderedRequiredKeys(_progressRequiredByKeyScratch, _progressOrderedKeysScratch);

        foreach (var ordered in _progressOrderedKeysScratch)
        {
            var key = (ordered.ProtoId, ordered.MatchMode);
            var required = _progressRequiredByKeyScratch.GetValueOrDefault(key, 0);
            _progressClaimableByKeyScratch[key] = required <= 0
                ? 0
                : ReserveProgressAcrossSources(
                    store,
                    user,
                    userItems,
                    crate,
                    crateItems,
                    storeNearbyItems,
                    hasCrateWork,
                    ordered.ProtoId,
                    ordered.MatchMode,
                    required);
        }
    }

    private void ApplyGroupedProgress(
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        int totalRequired
    )
    {
        var totalProgress = 0;

        foreach (var (key, indexes) in _progressTargetIndexesByKeyScratch)
        {
            var claimable = _progressClaimableByKeyScratch.GetValueOrDefault(key, 0);

            for (var i = 0; i < indexes.Count; i++)
            {
                var idx = indexes[i];
                var target = targets[idx];
                var required = Math.Max(0, target.Required);
                var progress = Math.Min(required, claimable);

                target.Progress = progress;
                targets[idx] = target;

                claimable -= progress;
                totalProgress = SaturatingAdd(totalProgress, progress);

                if (claimable <= 0)
                    break;
            }
        }

        contract.Required = totalRequired;
        contract.Progress = Math.Min(totalRequired, totalProgress);

        if (targets.Count > 0)
            contract.TargetItem = targets[0].TargetItem;

        SyncContractFlowStatus(contract);
    }

    private void UpdateSingleTargetContractProgress(
        ContractServerData contract,
        ContractTargetServerData target,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork)
    {
        contract.TargetItem = target.TargetItem;

        if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
        {
            target.Progress = 0;
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
        contract.Required = required;
        contract.Progress = progressed;
        SyncContractFlowStatus(contract);
    }

    private void UpdateLegacyContractProgress(
        ContractServerData contract,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        IReadOnlyList<EntityUid>? storeNearbyItems,
        bool hasCrateWork
    )
    {
        if (string.IsNullOrWhiteSpace(contract.TargetItem) || contract.Required <= 0)
        {
            contract.Progress = 0;
            SyncContractFlowStatus(contract);
            return;
        }

        var progressed = ComputeProgressForTarget(
            store,
            user,
            userItems,
            crate,
            crateItems,
            storeNearbyItems,
            hasCrateWork,
            contract.TargetItem,
            contract.MatchMode,
            contract.Required);

        contract.Progress = Math.Clamp(progressed, 0, contract.Required);
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
        int required)
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
        need -= ReserveProgressFromSource(store, storeNearbyItems, targetItem, matchMode, need, worldTurnInSource: true);

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

        return new List<int>(4);
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
            : ReserveProgressFromPrototypeItems(root, items, expectedProtoId, matchMode, need, virtualStackLeft, consumedNonStack, worldTurnInSource);
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
                !MatchesPrototypeId(candidateId, expectedProtoId, matchMode))
            {
                continue;
            }

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
