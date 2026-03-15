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

        if (!TryResolveClaimContract(store, contractId, out var comp, out var contract, out var targets, out fail))
            return false;

        PrepareClaimSources(store, user, contract, out var crateEntity, out var crateItems, out var storeNearbyItems);

        return targets.Count == 1
            ? TryPrepareSingleTargetClaimContext(
                store,
                user,
                contractId,
                comp,
                contract,
                targets,
                crateEntity,
                crateItems,
                storeNearbyItems,
                out ctx,
                out fail)
            : TryPrepareMultiTargetClaimContext(
                store,
                user,
                comp,
                contract,
                targets,
                crateEntity,
                crateItems,
                storeNearbyItems,
                out ctx,
                out fail);
    }

    private bool TryResolveClaimContract(
        EntityUid store,
        string contractId,
        out NcStoreComponent comp,
        out ContractServerData contract,
        out List<ContractTargetServerData> targets,
        out ClaimAttemptResult fail
    )
    {
        comp = default!;
        contract = default!;
        targets = default!;
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryResolveStoreComponentForClaim(store, out var storeComp, out fail))
            return false;

        if (!TryResolveClaimContractData(store, contractId, storeComp, out var foundContract, out fail))
            return false;

        if (!TryResolveClaimTargets(contractId, foundContract, out var effectiveTargets, out fail))
            return false;

        comp = storeComp;
        contract = foundContract;
        targets = effectiveTargets;
        return true;
    }

    private bool TryResolveStoreComponentForClaim(
        EntityUid store,
        out NcStoreComponent comp,
        out ClaimAttemptResult fail)
    {
        if (TryComp(store, out NcStoreComponent? storeComp))
        {
            comp = storeComp;
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);
            return true;
        }

        comp = default!;
        fail = ClaimAttemptResult.Fail(
            ClaimFailureReason.StoreMissing,
            $"Store {ToPrettyString(store)} has no NcStoreComponent.");
        return false;
    }

    private bool TryResolveClaimContractData(
        EntityUid store,
        string contractId,
        NcStoreComponent comp,
        out ContractServerData contract,
        out ClaimAttemptResult fail)
    {
        if (!comp.Contracts.TryGetValue(contractId, out contract!))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.ContractMissing,
                $"Store {ToPrettyString(store)} has no contract '{contractId}'.");
            return false;
        }

        if (contract.Taken)
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);
            return true;
        }

        fail = ClaimAttemptResult.Fail(
            ClaimFailureReason.NotTaken,
            $"Contract '{contractId}' is not taken yet.");
        return false;
    }

    private static bool TryResolveClaimTargets(
        string contractId,
        ContractServerData contract,
        out List<ContractTargetServerData> targets,
        out ClaimAttemptResult fail)
    {
        targets = GetEffectiveTargets(contract);
        if (targets.Count > 0)
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);
            return true;
        }

        fail = ClaimAttemptResult.Fail(
            ClaimFailureReason.NoValidTargets,
            $"Contract '{contractId}' has no valid targets.");
        return false;
    }

    private void PrepareClaimSources(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        out EntityUid? crateEntity,
        out List<EntityUid>? crateItems,
        out List<EntityUid> storeNearbyItems
    )
    {
        _logic.ScanInventoryItems(user, _scratchUserItems);

        crateEntity = null;
        crateItems = null;
        storeNearbyItems = _scratchStoreNearbyItems;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is { } pulledCrate && Exists(pulledCrate))
        {
            crateEntity = pulledCrate;
            _logic.ScanInventoryItems(pulledCrate, _scratchCrateItems);
            crateItems = _scratchCrateItems;
        }

        if (contract.AllowsStoreWorldTurnIn)
            ScanStoreNearbyTurnInItems(store, storeNearbyItems);
        else
            storeNearbyItems.Clear();
    }

    private bool TryPrepareMultiTargetClaimContext(
        EntityUid store,
        EntityUid user,
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
        if (!TryCollectClaimRequirements(targets, out fail))
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
        List<ContractTargetServerData> targets,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

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
            _claimRequiredByKeyScratch[key] = SaturatingAdd(_claimRequiredByKeyScratch.GetValueOrDefault(key, 0), target.Required);
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
    )
    {
        return new ClaimContext(
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

    private bool TryPrepareSingleTargetClaimContext(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        List<ContractTargetServerData> targets,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid>? storeNearbyItems,
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
                store,
                user,
                crateEntity,
                crateItems,
                storeNearbyItems,
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
        ctx = CreateClaimContext(store, user, crateEntity, comp, contract, targets, crateItems, takePlan);
        return true;
    }

    private bool TryAppendTakePlanForRequirement(
        EntityUid store,
        EntityUid user,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid>? storeNearbyItems,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required,
        List<ClaimTakeEntry> takePlan,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        var need = required;
        need -= AppendTakePlanFromSource(crateEntity, crateItems, targetItem, matchMode, need, takePlan);
        need -= AppendTakePlanFromSource(user, _scratchUserItems, targetItem, matchMode, need, takePlan);
        need -= AppendTakePlanFromSource(store, storeNearbyItems, targetItem, matchMode, need, takePlan, worldTurnInSource: true);

        if (need <= 0)
            return true;

        fail = CreateClaimPlanningFailure(crateEntity, storeNearbyItems, targetItem, matchMode, required, need);
        return false;
    }

    private int AppendTakePlanFromSource(
        EntityUid? root,
        List<EntityUid>? items,
        string targetItem,
        PrototypeMatchMode matchMode,
        int need,
        List<ClaimTakeEntry> takePlan,
        bool worldTurnInSource = false
    )
    {
        if (need <= 0 || root is not { } source || items == null)
            return 0;

        return ReserveTakePlanFromItems(
            source,
            items,
            targetItem,
            matchMode,
            need,
            _claimVirtualStackLeftScratch,
            takePlan,
            worldTurnInSource);
    }

    private static ClaimAttemptResult CreateClaimPlanningFailure(
        EntityUid? crateEntity,
        List<EntityUid>? storeNearbyItems,
        string targetItem,
        PrototypeMatchMode matchMode,
        int required,
        int missing
    )
    {
        if (crateEntity == null && (storeNearbyItems == null || storeNearbyItems.Count == 0))
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingCrate,
                $"need {required}x {targetItem} (mode={matchMode}), missing {missing}. Pull a closed crate to claim from it.");
        }

        return ClaimAttemptResult.Fail(
            ClaimFailureReason.NotEnoughItems,
            $"need {required}x {targetItem} (mode={matchMode}), missing {missing} after planning.");
    }

    private int ReserveTakePlanFromItems(
        EntityUid root,
        List<EntityUid> items,
        string expectedProtoId,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
        bool worldTurnInSource = false
    )
    {
        if (need <= 0)
            return 0;

        return TryGetStackTypeId(expectedProtoId, out var stackTypeId)
            ? ReserveTakePlanFromStackItems(root, items, stackTypeId, need, virtualStackLeft, planOut, worldTurnInSource)
            : ReserveTakePlanFromPrototypeItems(root, items, expectedProtoId, matchMode, need, virtualStackLeft, planOut, worldTurnInSource);
    }

    private int ReserveTakePlanFromStackItems(
        EntityUid root,
        List<EntityUid> items,
        string stackTypeId,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
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

            reserved += AppendClaimStackTake(root, ent, need - reserved, virtualStackLeft, planOut, items, i);
        }

        return reserved;
    }

    private int ReserveTakePlanFromPrototypeItems(
        EntityUid root,
        List<EntityUid> items,
        string expectedProtoId,
        PrototypeMatchMode matchMode,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
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
                reserved += AppendClaimStackTake(root, ent, need - reserved, virtualStackLeft, planOut, items, i);
                continue;
            }

            reserved += AppendClaimEntityTake(root, ent, planOut, items, i);
        }

        return reserved;
    }

    private int AppendClaimStackTake(
        EntityUid root,
        EntityUid ent,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        List<ClaimTakeEntry> planOut,
        List<EntityUid> items,
        int index
    )
    {
        var take = ReserveAvailableStackAmount(ent, need, virtualStackLeft, out var exhausted);
        if (exhausted)
            items[index] = EntityUid.Invalid;

        if (take <= 0)
            return 0;

        planOut.Add(new ClaimTakeEntry(root, ent, take, true));
        return take;
    }

    private static int AppendClaimEntityTake(
        EntityUid root,
        EntityUid ent,
        List<ClaimTakeEntry> planOut,
        List<EntityUid> items,
        int index
    )
    {
        planOut.Add(new ClaimTakeEntry(root, ent, 1, false));
        items[index] = EntityUid.Invalid;
        return 1;
    }
}
