using Content.Shared._NC.Trade;

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

        foreach (var (_, contract) in comp.Contracts)
        {
            var hasCrateWork = PrepareProgressWorkItems(userItems, crate, crateItems);
            ClearProgressPerContractScratch();
            UpdateContractProgressForSingleContract(contract, user, crate, hasCrateWork);
        }
    }

    private void UpdateContractProgressForSingleContract(
        ContractServerData contract,
        EntityUid user,
        EntityUid? crate,
        bool hasCrateWork
    )
    {
        var targets = GetEffectiveTargets(contract);

        if (targets.Count == 0)
        {
            UpdateLegacyContractProgress(contract, user, crate, hasCrateWork);
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

            if (crate is { } crateRoot && hasCrateWork)
            {
                var reserved = ReserveTakePlanFromItems(
                    crateRoot,
                    _progressCrateItemsScratch,
                    ordered.ProtoId,
                    ordered.MatchMode,
                    need,
                    _progressVirtualStackLeftScratch,
                    _progressSimulatedPlanScratch);

                need -= reserved;
            }

            if (need > 0)
            {
                var reserved = ReserveTakePlanFromItems(
                    user,
                    _progressUserItemsScratch,
                    ordered.ProtoId,
                    ordered.MatchMode,
                    need,
                    _progressVirtualStackLeftScratch,
                    _progressSimulatedPlanScratch);

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
        EntityUid? crate,
        bool hasCrateWork
    )
    {
        if (string.IsNullOrWhiteSpace(contract.TargetItem) || contract.Required <= 0)
        {
            contract.Progress = 0;
            return;
        }

        var need = contract.Required;

        if (crate is { } crateRoot && hasCrateWork)
        {
            var reserved = ReserveTakePlanFromItems(
                crateRoot,
                _progressCrateItemsScratch,
                contract.TargetItem,
                contract.MatchMode,
                need,
                _progressVirtualStackLeftScratch,
                _progressSimulatedPlanScratch);

            need -= reserved;
        }

        if (need > 0)
        {
            var reserved = ReserveTakePlanFromItems(
                user,
                _progressUserItemsScratch,
                contract.TargetItem,
                contract.MatchMode,
                need,
                _progressVirtualStackLeftScratch,
                _progressSimulatedPlanScratch);

            need -= reserved;
        }

        var progressed = contract.Required - Math.Max(0, need);
        contract.Progress = Math.Clamp(progressed, 0, contract.Required);
    }

    private bool PrepareProgressWorkItems(
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems
    )
    {
        _progressUserItemsScratch.Clear();
        _progressUserItemsScratch.AddRange(userItems);

        _progressCrateItemsScratch.Clear();

        if (crate is null || crateItems is not { Count: > 0 })
            return false;

        _progressCrateItemsScratch.AddRange(crateItems);
        return _progressCrateItemsScratch.Count > 0;
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
        _progressSimulatedPlanScratch.Clear();
        _progressOrderedKeysScratch.Clear();
    }

    private List<int> RentProgressTargetIndexList()
    {
        if (_progressTargetIndexPool.Count > 0)
            return _progressTargetIndexPool.Pop();

        return new List<int>(4);
    }
}
