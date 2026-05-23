using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private ClaimAttemptResult TryClaimTrackedDeliveryContract(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        EnsureObjectiveRuntimeDefaults(contract);

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return CreateTrackedDeliveryTargetLostResult();

        return UsesTrackedDeliveryDropoff(contract)
            ? TryClaimTrackedDeliveryDropoff(store, user, contractId, comp, contract, key, state)
            : TryClaimTrackedDeliveryStoreTarget(store, user, contractId, comp, contract, key, state);
    }

    private ClaimAttemptResult TryClaimTrackedDeliveryDropoff(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (state.DeliveryDropoffCompleted)
            SetTrackedDeliveryProgress(contract, GetTrackedDeliveryCompletionAmount(contract));

        if (!contract.Completed)
        {
            if (!TryGetLiveTrackedDeliveryTarget(state, out var target))
                return FailTrackedDeliveryObjective(key, comp, contract);

            var trackedAmount = IsTrackedDeliveryTargetAtDropoff(target, state)
                ? GetTrackedDeliveryAmount(contract, target)
                : 0;
            SetTrackedDeliveryProgress(contract, trackedAmount);
        }

        if (!contract.Completed)
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery target for '{contractId}' has not reached the dropoff point.");
        }

        return CompleteTrackedDeliveryClaim(store, user, contractId, comp, contract);
    }

    private ClaimAttemptResult TryClaimTrackedDeliveryStoreTarget(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (!TryGetLiveTrackedDeliveryTarget(state, out var target))
            return FailTrackedDeliveryObjective(key, comp, contract);

        ScanTrackedDeliveryTransferSources(user, out var userItems, out var crateEntity, out var crateItems);

        var inUserInventory = ContainsTrackedDeliveryEntity(userItems, target);
        var inCrate = ContainsTrackedDeliveryEntity(crateItems, target);
        var atStore = IsTrackedDeliveryTargetAtStore(store, target);
        if (!inUserInventory && !inCrate && !atStore)
        {
            SetTrackedDeliveryProgress(contract, 0);
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery target for '{contractId}' is not present in user inventory, pulled crate or at the store.");
        }

        if (IsTrackedDeliveryProtectedFromDirectSale(user, target, crateEntity, inUserInventory, inCrate))
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery target for '{contractId}' is protected from direct sale.");
        }

        SetTrackedDeliveryProgress(contract, GetTrackedDeliveryAmount(contract, target));
        if (!contract.Completed)
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Tracked delivery progress {contract.Progress}/{contract.Required} for '{contractId}'.");
        }

        return CompleteTrackedDeliveryClaim(store, user, contractId, comp, contract);
    }

    private ClaimAttemptResult CreateTrackedDeliveryTargetLostResult() =>
        ClaimAttemptResult.Fail(
            ClaimFailureReason.ObjectiveFailed,
            Loc.GetString("nc-store-contract-delivery-target-lost"));

    private ClaimAttemptResult FailTrackedDeliveryObjective(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);

        return CreateTrackedDeliveryTargetLostResult();
    }

    private ClaimAttemptResult CompleteTrackedDeliveryClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (!TryValidateContractRewards(user, contract.Rewards, out var rewardFail))
            return rewardFail;

        var config = contract.Config;
        var objectiveJournal = new ObjectiveConsumeJournal();
        if (!TryGiveContractRewardsWithPreCommit(
            user,
            contract.Rewards,
            () => TryConsumeObjectiveProof(store, user, contractId, contract, objectiveJournal, out var proofFail)
                ? ClaimAttemptResult.Ok()
                : proofFail,
            out var rewardExecFail))
        {
            RollbackObjectiveConsumeJournal(objectiveJournal);
            return rewardExecFail;
        }

        CommitObjectiveConsumeJournal(objectiveJournal);
        FinalizeClaim(
            store,
            comp,
            contractId,
            contract.Repeatable,
            !config.PreserveTargetOnComplete);
        return ClaimAttemptResult.Ok();
    }
}
