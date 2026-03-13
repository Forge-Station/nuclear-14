using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

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
        List<EntityUid> UserItems,
        List<EntityUid>? CrateItems,
        List<ClaimTakeEntry> TakePlan
    );

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
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.StoreMissing,
                $"Store {ToPrettyString(store)} has no NcStoreComponent.");
            return false;
        }

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.ContractMissing,
                $"Store {ToPrettyString(store)} has no contract '{contractId}'.");
            return false;
        }

        if (!contract.Taken)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.NotTaken,
                $"Contract '{contractId}' is not taken yet.");
            return false;
        }

        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.NoValidTargets,
                $"Contract '{contractId}' has no valid targets.");
            return false;
        }

        _logic.ScanInventoryItems(user, _scratchUserItems);

        EntityUid? crateEntity = null;
        List<EntityUid>? crateItems = null;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is { } c0 && Exists(c0))
        {
            crateEntity = c0;
            _logic.ScanInventoryItems(c0, _scratchCrateItems);
            crateItems = _scratchCrateItems;
        }

        if (targets.Count == 1)
        {
            return TryPrepareSingleTargetClaimContext(
                store,
                user,
                contractId,
                comp,
                contract,
                targets,
                crateEntity,
                crateItems,
                out ctx,
                out fail);
        }

        ClearClaimPlanningScratch();

        foreach (var t in targets)
        {
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
            {
                ClearClaimPlanningScratch();
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.InvalidTarget,
                    $"Invalid target '{t.TargetItem}' (required={t.Required}).");
                return false;
            }

            var key = (t.TargetItem, t.MatchMode);
            _claimRequiredByKeyScratch[key] = SaturatingAdd(_claimRequiredByKeyScratch.GetValueOrDefault(key, 0), t.Required);
        }

        var takePlan = new List<ClaimTakeEntry>(Math.Max(8, Math.Min(64, targets.Count * 4)));
        BuildOrderedRequiredKeys(_claimRequiredByKeyScratch, _claimOrderedKeysScratch);

        foreach (var ordered in _claimOrderedKeysScratch)
        {
            var key = (ordered.ProtoId, ordered.MatchMode);
            var required = _claimRequiredByKeyScratch.GetValueOrDefault(key, 0);
            if (required <= 0)
                continue;

            if (!TryAppendTakePlanForRequirement(
                    user,
                    crateEntity,
                    crateItems,
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

        ctx = new ClaimContext(
            store,
            user,
            crateEntity,
            comp,
            contract,
            targets,
            _scratchUserItems,
            crateItems,
            takePlan);

        return true;
    }

    private bool TryPrepareSingleTargetClaimContext(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        out ClaimContext ctx,
        out ClaimAttemptResult fail)
    {
        ctx = default;
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var target = targets[0];
        if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.InvalidTarget,
                $"Invalid target '{target.TargetItem}' (required={target.Required}).");
            return false;
        }

        ClearClaimPlanningScratch();
        var takePlan = new List<ClaimTakeEntry>(Math.Max(4, Math.Min(32, target.Required)));

        if (!TryAppendTakePlanForRequirement(
                user,
                crateEntity,
                crateItems,
                target.TargetItem,
                target.MatchMode,
                target.Required,
                takePlan,
                out fail))
        {
            ClearClaimPlanningScratch();
            return false;
        }

        ClearClaimPlanningScratch();

        ctx = new ClaimContext(
            store,
            user,
            crateEntity,
            comp,
            contract,
            targets,
            _scratchUserItems,
            crateItems,
            takePlan);

        return true;
    }

    private bool TryAppendTakePlanForRequirement(
        EntityUid user,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required,
        List<ClaimTakeEntry> takePlan,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var need = required;

        if (crateEntity is { } crate && crateItems != null)
        {
            var reserved = ReserveTakePlanFromItems(
                crate,
                crateItems,
                targetItem,
                matchMode,
                need,
                _claimVirtualStackLeftScratch,
                takePlan);

            need -= reserved;
        }

        if (need > 0)
        {
            var reserved = ReserveTakePlanFromItems(
                user,
                _scratchUserItems,
                targetItem,
                matchMode,
                need,
                _claimVirtualStackLeftScratch,
                takePlan);

            need -= reserved;
        }

        if (need <= 0)
            return true;

        if (crateEntity == null)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingCrate,
                $"need {required}x {targetItem} (mode={matchMode}), missing {need}. Pull a closed crate to claim from it.");
            return false;
        }

        fail = ClaimAttemptResult.Fail(
            ClaimFailureReason.NotEnoughItems,
            $"need {required}x {targetItem} (mode={matchMode}), missing {need} after planning.");
        return false;
    }

    private int ReserveTakePlanFromItems(
        EntityUid root,
        List<EntityUid> items,
        string expectedProtoId,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut
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

                var have = virtualStackLeft.TryGetValue(ent, out var v)
                    ? v
                    : Math.Max(stack.Count, 0);

                if (have <= 0)
                {
                    items[i] = EntityUid.Invalid;
                    continue;
                }

                var take = Math.Min(have, need - reserved);
                if (take <= 0)
                    continue;

                planOut.Add(new ClaimTakeEntry(root, ent, take, true));
                reserved += take;

                var left = have - take;
                if (left > 0)
                    virtualStackLeft[ent] = left;
                else
                {
                    virtualStackLeft.Remove(ent);
                    items[i] = EntityUid.Invalid;
                }
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

            if (!MatchesPrototypeId(meta.EntityPrototype.ID, expectedProtoId, matchMode))
                continue;

            if (TryComp(ent, out StackComponent? st) && st.Count > 0)
            {
                var have = virtualStackLeft.TryGetValue(ent, out var v)
                    ? v
                    : Math.Max(st.Count, 0);

                if (have <= 0)
                {
                    items[i] = EntityUid.Invalid;
                    continue;
                }

                var take = Math.Min(have, need - reserved);
                if (take <= 0)
                    continue;

                planOut.Add(new ClaimTakeEntry(root, ent, take, true));
                reserved += take;

                var left = have - take;
                if (left > 0)
                    virtualStackLeft[ent] = left;
                else
                {
                    virtualStackLeft.Remove(ent);
                    items[i] = EntityUid.Invalid;
                }

                continue;
            }

            planOut.Add(new ClaimTakeEntry(root, ent, 1, false));
            reserved += 1;
            items[i] = EntityUid.Invalid;
        }

        return reserved;
    }
}

