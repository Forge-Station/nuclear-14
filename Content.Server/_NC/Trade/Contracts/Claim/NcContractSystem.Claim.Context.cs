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

        if (RequiresRetrievalSpawnedTurnIn(contract))
        {
            RefreshRetrievalSpawnedProgressForClaim(
                store,
                user,
                contractId,
                contract,
                crateEntity,
                crateItems,
                storeNearbyItems);

            return TryPrepareRetrievalSpawnedClaimContext(
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
                out fail);
        }

        RefreshInventoryDeliveryProgressForClaim(
            store,
            user,
            contractId,
            contract,
            crateEntity,
            crateItems,
            storeNearbyItems);

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
                contractId,
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
            // Include the pulled crate entity itself: some retrieval contracts require delivering
            // the container prototype, not only items inside it.
            _scratchCrateItems.Add(pulledCrate);
            crateItems = _scratchCrateItems;
        }

        if (contract.AllowsStoreWorldTurnIn)
            ScanStoreNearbyTurnInItems(store, storeNearbyItems);
        else
            storeNearbyItems.Clear();
    }

    private void RefreshInventoryDeliveryProgressForClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        EntityUid? crateEntity,
        List<EntityUid>? crateItems,
        List<EntityUid> storeNearbyItems)
    {
        if (_progressScratchInUse)
        {
            Sawmill.Warning(
                $"[Claim] Progress refresh for '{contractId}' on {ToPrettyString(store)} skipped because progress scratch is already in use. " +
                "Claim planning will still validate the current inventory state.");
            return;
        }

        _progressScratchInUse = true;
        try
        {
            UpdateContractProgressForSingleContract(
                contract,
                store,
                user,
                _scratchUserItems,
                crateEntity,
                crateItems,
                storeNearbyItems,
                crateEntity != null && crateItems is { Count: > 0 });
            ApplyPartialTurnInProgress(store, contractId, contract);
        }
        finally
        {
            _progressScratchInUse = false;
        }
    }
}
