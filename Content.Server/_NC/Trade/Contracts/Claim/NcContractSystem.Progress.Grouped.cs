using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
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
}
