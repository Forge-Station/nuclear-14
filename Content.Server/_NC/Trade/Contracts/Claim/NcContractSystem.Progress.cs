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
        IReadOnlyList<EntityUid>? crateItems
    )
    {
        if (comp.Contracts.Count == 0)
            return;

        _progressContractIdsScratch.Clear();
        foreach (var contractId in comp.Contracts.Keys)
            _progressContractIdsScratch.Add(contractId);

        var hasCrateWork = crate is { } && crateItems is { Count: > 0 };

        for (var i = 0; i < _progressContractIdsScratch.Count; i++)
        {
            var contractId = _progressContractIdsScratch[i];
            if (!comp.Contracts.TryGetValue(contractId, out var contract))
                continue;

            if (!contract.Taken)
            {
                ResetContractProgress(contract);
                continue;
            }

            switch (contract.ExecutionKind)
            {
                case ContractExecutionKind.TrackedDeliveryObjective:
                    UpdateTrackedDeliveryObjectiveProgress(store, contractId, contract, userItems, crateItems);
                    continue;

                case ContractExecutionKind.HuntObjective:
                case ContractExecutionKind.RepairObjective:
                case ContractExecutionKind.GhostRoleObjective:
                    UpdateObjectiveContractProgress(store, contractId, contract);
                    continue;
            }

            UpdateContractProgressForSingleContract(contract, user, userItems, crate, crateItems, hasCrateWork);
        }

        _progressContractIdsScratch.Clear();
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
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool hasCrateWork
    )
    {
        var targets = GetEffectiveTargets(contract);

        if (targets.Count == 0)
        {
            ClearProgressReservationScratch();
            UpdateLegacyContractProgress(contract, user, userItems, crate, crateItems, hasCrateWork);
            return;
        }

        if (targets.Count == 1)
        {
            ClearProgressReservationScratch();
            UpdateSingleTargetContractProgress(contract, targets[0], user, userItems, crate, crateItems, hasCrateWork);
            return;
        }

        ClearProgressPerContractScratch();

        var totalRequired = 0;

        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];

            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
            {
                t.Progress = 0;
                targets[i] = t;
                continue;
            }

            var key = (t.TargetItem, t.MatchMode);
            _progressRequiredByKeyScratch[key] = SaturatingAdd(_progressRequiredByKeyScratch.GetValueOrDefault(key, 0), t.Required);

            if (!_progressTargetIndexesByKeyScratch.TryGetValue(key, out var indexes))
            {
                indexes = RentProgressTargetIndexList();
                _progressTargetIndexesByKeyScratch[key] = indexes;
            }

            indexes.Add(i);
            totalRequired = SaturatingAdd(totalRequired, t.Required);
        }

        if (_progressRequiredByKeyScratch.Count == 0)
        {
            contract.Required = 0;
            contract.Progress = 0;
            if (targets.Count > 0)
                contract.TargetItem = targets[0].TargetItem;

            SyncContractFlowStatus(contract);
            return;
        }

        foreach (var (key, required) in _progressRequiredByKeyScratch)
        {
            if (required <= 0)
                _progressClaimableByKeyScratch[key] = 0;
        }

        BuildOrderedRequiredKeys(_progressRequiredByKeyScratch, _progressOrderedKeysScratch);

        foreach (var ordered in _progressOrderedKeysScratch)
        {
            var key = (ordered.ProtoId, ordered.MatchMode);
            var required = _progressRequiredByKeyScratch.GetValueOrDefault(key, 0);
            if (required <= 0)
            {
                _progressClaimableByKeyScratch[key] = 0;
                continue;
            }

            var need = required;

            if (crate is { } crateRoot && hasCrateWork && crateItems != null)
            {
                var reserved = ReserveProgressFromItems(
                    crateRoot,
                    crateItems,
                    ordered.ProtoId,
                    ordered.MatchMode,
                    need,
                    _progressVirtualStackLeftScratch,
                    _progressConsumedEntitiesScratch);

                need -= reserved;
            }

            if (need > 0)
            {
                var reserved = ReserveProgressFromItems(
                    user,
                    userItems,
                    ordered.ProtoId,
                    ordered.MatchMode,
                    need,
                    _progressVirtualStackLeftScratch,
                    _progressConsumedEntitiesScratch);

                need -= reserved;
            }

            var claimable = required - Math.Max(0, need);
            _progressClaimableByKeyScratch[key] = Math.Max(0, claimable);
        }

        var totalProgress = 0;

        foreach (var (key, indexes) in _progressTargetIndexesByKeyScratch)
        {
            var claimable = _progressClaimableByKeyScratch.GetValueOrDefault(key, 0);

            for (var i = 0; i < indexes.Count; i++)
            {
                var idx = indexes[i];
                var t = targets[idx];

                var required = Math.Max(0, t.Required);
                var progress = Math.Min(required, claimable);

                t.Progress = progress;
                targets[idx] = t;

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
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
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
            user,
            userItems,
            crate,
            crateItems,
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
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
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
            user,
            userItems,
            crate,
            crateItems,
            hasCrateWork,
            contract.TargetItem,
            contract.MatchMode,
            contract.Required);

        contract.Progress = Math.Clamp(progressed, 0, contract.Required);
        SyncContractFlowStatus(contract);
    }

    private int ComputeProgressForTarget(
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool hasCrateWork,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required)
    {
        if (string.IsNullOrWhiteSpace(targetItem) || required <= 0)
            return 0;

        var need = required;

        if (crate is { } crateRoot && hasCrateWork && crateItems != null)
        {
            var reserved = ReserveProgressFromItems(
                crateRoot,
                crateItems,
                targetItem,
                matchMode,
                need,
                _progressVirtualStackLeftScratch,
                _progressConsumedEntitiesScratch);

            need -= reserved;
        }

        if (need > 0)
        {
            var reserved = ReserveProgressFromItems(
                user,
                userItems,
                targetItem,
                matchMode,
                need,
                _progressVirtualStackLeftScratch,
                _progressConsumedEntitiesScratch);

            need -= reserved;
        }

        var progressed = required - Math.Max(0, need);
        return Math.Clamp(progressed, 0, required);
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
        HashSet<EntityUid> consumedNonStack
    )
    {
        if (need <= 0)
            return 0;

        var reserved = 0;

        if (TryGetStackTypeId(expectedProtoId, out var stackTypeId))
        {
            for (var i = 0; i < items.Count && reserved < need; i++)
            {
                var ent = items[i];
                if (ent == EntityUid.Invalid || !EntityManager.EntityExists(ent))
                    continue;

                if (_logic.IsProtectedFromDirectSale(root, ent))
                    continue;

                if (!TryComp(ent, out StackComponent? stack) || stack.StackTypeId != stackTypeId)
                    continue;

                var have = virtualStackLeft.TryGetValue(ent, out var virtualLeft)
                    ? virtualLeft
                    : Math.Max(stack.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, need - reserved);
                if (take <= 0)
                    continue;

                reserved += take;
                virtualStackLeft[ent] = have - take;
            }

            return reserved;
        }

        for (var i = 0; i < items.Count && reserved < need; i++)
        {
            var ent = items[i];
            if (ent == EntityUid.Invalid || !EntityManager.EntityExists(ent))
                continue;

            if (_logic.IsProtectedFromDirectSale(root, ent))
                continue;

            if (!TryComp(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                continue;

            var candidateId = meta.EntityPrototype.ID;
            var matches = MatchesPrototypeId(candidateId, expectedProtoId, matchMode);

            if (!matches)
                continue;

            if (TryComp(ent, out StackComponent? stack) && stack.Count > 0)
            {
                var have = virtualStackLeft.TryGetValue(ent, out var virtualLeft)
                    ? virtualLeft
                    : Math.Max(stack.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, need - reserved);
                if (take <= 0)
                    continue;

                reserved += take;
                virtualStackLeft[ent] = have - take;
                continue;
            }

            if (!consumedNonStack.Add(ent))
                continue;

            reserved += 1;
        }

        return reserved;
    }
}

