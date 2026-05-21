using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryPrepareMultiTargetClaimContext(
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
        if (!TryCollectClaimRequirements(store, contractId, targets, out fail))
            return false;

        var takePlan = new List<ClaimTakeEntry>(Math.Max(8, Math.Min(64, targets.Count * 4)));
        BuildOrderedRequiredKeys(_claimRequiredByKeyScratch, _claimOrderedKeysScratch);

        foreach (var ordered in _claimOrderedKeysScratch)
        {
            var key = (ordered.ProtoId, ordered.MatchMode);
            var required = _claimRequiredByKeyScratch.GetValueOrDefault(key, 0);
            if (required <= 0)
                continue;

            if (!TryAppendTakePlanForRequirement(
                store,
                user,
                crateEntity,
                crateItems,
                storeNearbyItems,
                ordered.ProtoId,
                ordered.MatchMode,
                required,
                takePlan,
                out fail))
            {
                ClearClaimPlanningScratch();
                return false;
            }
        }

        ClearClaimPlanningScratch();
        ctx = CreateClaimContext(store, user, crateEntity, comp, contract, targets, crateItems, takePlan);
        return true;
    }

    private bool TryCollectClaimRequirements(
        EntityUid store,
        string contractId,
        List<ContractTargetServerData> targets,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var turnedInLeftByKey = new Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int>();
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
            {
                ClearClaimPlanningScratch();
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.InvalidTarget,
                    $"Invalid target '{target.TargetItem}' (required={target.Required}).");
                return false;
            }

            var key = (target.TargetItem, target.MatchMode);
            var remaining = GetRemainingTurnInRequirement(store, contractId, target, turnedInLeftByKey);
            if (remaining <= 0)
                continue;

            _claimRequiredByKeyScratch[key] = SaturatingAdd(
                _claimRequiredByKeyScratch.GetValueOrDefault(key, 0),
                remaining);
        }

        return true;
    }

    private ClaimContext CreateClaimContext(
        EntityUid store,
        EntityUid user,
        EntityUid? crateEntity,
        NcStoreComponent comp,
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        List<EntityUid>? crateItems,
        List<ClaimTakeEntry> takePlan
    ) =>
        new(
            store,
            user,
            crateEntity,
            comp,
            contract,
            targets,
            _scratchUserItems,
            crateItems,
            takePlan);
}
