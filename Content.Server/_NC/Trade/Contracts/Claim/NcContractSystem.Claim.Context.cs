using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly record struct ClaimContext(
        EntityUid Store,
        EntityUid User,
        EntityUid? Crate,
        NcStoreComponent Comp,
        ContractServerData Contract,
        List<ContractTargetServerData> Targets,
        Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> RequiredByKey,
        NcInventorySnapshot UserSnap,
        List<EntityUid> UserItems,
        NcInventorySnapshot? CrateSnap,
        List<EntityUid>? CrateItems);

    private bool TryPrepareClaimContext(
        EntityUid store,
        EntityUid user,
        string contractId,
        out ClaimContext ctx,
        out ClaimAttemptResult fail
    )
    {
        ctx = default;
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryComp(store, out NcStoreComponent? comp))
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.StoreMissing, $"Store {ToPrettyString(store)} has no NcStoreComponent.");
            return false;
        }

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.ContractMissing, $"Store {ToPrettyString(store)} has no contract '{contractId}'.");
            return false;
        }

        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.NoValidTargets, $"Contract '{contractId}' has no valid targets.");
            return false;
        }

        var requiredByKey = new Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int>();
        foreach (var t in targets)
        {
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
            {
                fail = ClaimAttemptResult.Fail(ClaimFailureReason.InvalidTarget, $"Invalid target '{t.TargetItem}' (required={t.Required}).");
                return false;
            }

            var key = (t.TargetItem, t.MatchMode);
            if (!requiredByKey.TryAdd(key, t.Required))
                requiredByKey[key] = checked(requiredByKey[key] + t.Required);
        }

        _logic.ScanInventory(user, _scratchUserItems, _scratchUserSnap);
        var userSnap = _scratchUserSnap;

        EntityUid? crateEntity = null;
        NcInventorySnapshot? crateSnap = null;
        List<EntityUid>? crateItems = null;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is { } c0 && Exists(c0))
        {
            crateEntity = c0;
            _logic.ScanInventory(c0, _scratchCrateItems, _scratchCrateSnap);
            crateSnap = _scratchCrateSnap;
            crateItems = _scratchCrateItems;
        }

        foreach (var kvp in requiredByKey)
        {
            var (protoId, matchMode) = kvp.Key;
            var required = kvp.Value;

            var ownedUser = _logic.GetOwnedFromSnapshot(userSnap, protoId, matchMode);

            var ownedCrate = crateSnap != null
                ? _logic.GetOwnedFromSnapshot(crateSnap, protoId, matchMode)
                : 0;

            if (ownedUser + ownedCrate < required)
            {
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.NotEnoughItems,
                    $"need {required}x {protoId} (mode={matchMode}), have user={ownedUser}, crate={ownedCrate}"
                );
                return false;
            }
        }
        ctx = new ClaimContext(
            store,
            user,
            crateEntity,
            comp,
            contract,
            targets,
            requiredByKey,
            userSnap,
            _scratchUserItems,
            crateSnap,
            crateItems);
        return true;
    }
}
