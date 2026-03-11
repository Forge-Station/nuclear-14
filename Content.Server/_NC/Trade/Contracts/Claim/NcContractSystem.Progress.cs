using Content.Shared._NC.Trade;
using Content.Shared.Stacks;


namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public void UpdateContractsProgress(
        NcStoreComponent comp,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems
    )
    {
        if (comp.Contracts.Count == 0)
            return;

        var hasCrateWork = crate is { } && crateItems is { Count: > 0 };

        foreach (var (_, contract) in comp.Contracts)
        {
            ClearProgressPerContractScratch();
            UpdateContractProgressForSingleContract(contract, user, userItems, crate, crateItems, hasCrateWork);
        }
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
            UpdateLegacyContractProgress(contract, user, userItems, crate, crateItems, hasCrateWork);
            return;
        }

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
            return;
        }

        foreach (var (key, required) in _progressRequiredByKeyScratch)
        {
            if (required <= 0)
            {
                _progressClaimableByKeyScratch[key] = 0;
                continue;
            }

            _progressOrderedKeysScratch.Add((key.ProtoId, key.MatchMode, GetProtoDepth(key.ProtoId)));
        }

        _progressOrderedKeysScratch.Sort(static (a, b) =>
        {
            var depth = b.Depth.CompareTo(a.Depth);
            if (depth != 0)
                return depth;

            var mode = ((int) a.MatchMode).CompareTo((int) b.MatchMode);
            if (mode != 0)
                return mode;

            return string.CompareOrdinal(a.ProtoId, b.ProtoId);
        });

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
            return;
        }

        var need = contract.Required;

        if (crate is { } crateRoot && hasCrateWork && crateItems != null)
        {
            var reserved = ReserveProgressFromItems(
                crateRoot,
                crateItems,
                contract.TargetItem,
                contract.MatchMode,
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
                contract.TargetItem,
                contract.MatchMode,
                need,
                _progressVirtualStackLeftScratch,
                _progressConsumedEntitiesScratch);

            need -= reserved;
        }

        var progressed = contract.Required - Math.Max(0, need);
        contract.Progress = Math.Clamp(progressed, 0, contract.Required);
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
        _progressVirtualStackLeftScratch.Clear();
        _progressConsumedEntitiesScratch.Clear();
        _progressOrderedKeysScratch.Clear();
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
            var matches = matchMode == PrototypeMatchMode.Exact
                ? candidateId == expectedProtoId
                : candidateId == expectedProtoId || IsDescendantId(candidateId, expectedProtoId);

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
