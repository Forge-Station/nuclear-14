using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
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
            return;

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
            contract.Required = 0;
            contract.Progress = 0;
            SyncContractFlowStatus(contract);
            return true;
        }

        if (targets.Count != 1)
            return false;

        ClearProgressReservationScratch();
        UpdateSingleTargetContractProgress(
            contract,
            targets,
            0,
            store,
            user,
            userItems,
            crate,
            crateItems,
            storeNearbyItems,
            hasCrateWork);
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
            _progressRequiredByKeyScratch[key] = SaturatingAdd(
                _progressRequiredByKeyScratch.GetValueOrDefault(key, 0),
                target.Required);

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
}
