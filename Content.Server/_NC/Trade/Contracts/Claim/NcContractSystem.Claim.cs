using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    public bool TryClaim(EntityUid store, EntityUid user, string contractId)
    {
        var claimKey = (store, user, contractId);
        if (!_claimInProgress.Add(claimKey))
        {
            Sawmill.Warning(
                $"[Claim] Duplicate/re-entrant TryClaim for '{contractId}' by {ToPrettyString(user)} on {ToPrettyString(store)} rejected.");
            return false;
        }

        if (_claimScratchInUse)
        {
            _claimInProgress.Remove(claimKey);
            Sawmill.Warning(
                $"[Claim] Nested TryClaim for '{contractId}' by {ToPrettyString(user)} on {ToPrettyString(store)} rejected. " +
                "Claim planning scratch is already in use; check event handlers.");
            return false;
        }

        _claimScratchInUse = true;
        try
        {
            var res = TryClaimDetailed(store, user, contractId);
            if (!res.Success)
            {
                if (res.Reason is ClaimFailureReason.NotEnoughItems
                    or ClaimFailureReason.NoValidTargets
                    or ClaimFailureReason.MissingCrate
                    or ClaimFailureReason.MissingBody
                    or ClaimFailureReason.MissingProof
                    or ClaimFailureReason.ObjectiveNotCompleted)
                {
                    Sawmill.Info(
                        $"[Claim] Failed ({res.Reason}) '{contractId}' on {ToPrettyString(store)}: {res.Details}");
                }
                else
                {
                    Sawmill.Warning(
                        $"[Claim] Failed ({res.Reason}) '{contractId}' on {ToPrettyString(store)}: {res.Details}");
                }
            }

            return res.Success;
        }
        finally
        {
            _claimScratchInUse = false;
            _claimInProgress.Remove(claimKey);
        }
    }

    private ClaimAttemptResult TryClaimDetailed(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.StoreMissing,
                $"Store {ToPrettyString(store)} has no NcStoreComponent.");
        }

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ContractMissing,
                $"Store {ToPrettyString(store)} has no contract '{contractId}'.");
        }

        if (!contract.Taken)
            return ClaimAttemptResult.Fail(ClaimFailureReason.NotTaken, $"Contract '{contractId}' is not taken yet.");

        if (!TryEvaluateContractConditions(
            ContractConditionPhase.Claim,
            store,
            user,
            contractId,
            contract,
            out var conditionFailure))
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.InvalidTarget,
                string.IsNullOrWhiteSpace(conditionFailure)
                    ? $"Claim conditions are not satisfied for '{contractId}'."
                    : conditionFailure);
        }

        if (TryGetObjectiveHandler(contract.ExecutionKind, out var handler))
            return handler.TryClaim(this, store, user, contractId, comp, contract);

        return ClaimAttemptResult.Fail(
            ClaimFailureReason.ExecutionFailed,
            $"No objective handler registered for execution kind {contract.ExecutionKind}.");
    }


    private static bool RequiresRetrievalRouteRewardClaim(ContractServerData contract) =>
        RequiresRetrievalRouteDelivery(contract);

    private ClaimAttemptResult TryClaimInventoryDeliveryContract(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (RequiresRetrievalRouteRewardClaim(contract))
            return TryClaimRetrievalRouteReward(store, user, contractId, comp, contract);

        if (!TryPrepareClaimContext(store, user, contractId, out var ctx, out var prepFail))
        {
            if (TryPreparePartialClaimContext(store, user, contractId, out var partialCtx, out var partialPrepFail))
            {
                return TryExecutePartialClaimTakePlan(contractId, partialCtx, out var partialExecFail)
                    ? ClaimAttemptResult.Ok()
                    : partialExecFail;
            }

            if (partialPrepFail.Reason != ClaimFailureReason.None &&
                prepFail.Reason is ClaimFailureReason.NotEnoughItems or ClaimFailureReason.MissingCrate)
                return partialPrepFail;

            return prepFail;
        }

        if (!TryExecuteClaimTakePlan(ctx, out var execFail))
            return execFail;

        FinalizeClaim(ctx.Store, ctx.Comp, contractId, ctx.Contract.Repeatable);
        return ClaimAttemptResult.Ok();
    }

    private ClaimAttemptResult TryClaimRetrievalRouteReward(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        RefreshRetrievalRouteDeliveryForClaim(store, contractId, contract);

        if (!contract.Completed)
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Retrieval route cargo for '{contractId}' has not been fully delivered yet.");
        }

        if (!TryValidateContractRewards(user, contract.Rewards, out var rewardFail))
            return rewardFail;

        var objectiveJournal = new ObjectiveConsumeJournal();
        if (!TryGiveContractRewardsWithPreCommit(
            user,
            contract.Rewards,
            () =>
            {
                if (contract.Config.RetrievalClaimMode != NcRetrievalClaimMode.DestinationProof)
                    return ClaimAttemptResult.Ok();

                return TryConsumeObjectiveProof(store, user, contractId, contract, objectiveJournal, out var proofFail)
                    ? ClaimAttemptResult.Ok()
                    : proofFail;
            },
            out var rewardExecFail))
        {
            RollbackObjectiveConsumeJournal(objectiveJournal);
            return rewardExecFail;
        }

        CommitObjectiveConsumeJournal(objectiveJournal);
        FinalizeClaim(store, comp, contractId, contract.Repeatable, contract.Config.RetrievalConsumeCargo);
        return ClaimAttemptResult.Ok();
    }

    private ClaimAttemptResult TryClaimObjectiveContract(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        EnsureObjectiveRuntimeDefaults(contract);
        UpdateObjectiveContractProgress(store, contractId, contract);

        if (!TryValidateObjectiveClaim(store, user, contractId, contract, out var validationFail))
            return validationFail;

        if (!TryExecuteObjectiveClaimRewards(store, user, contractId, contract, out var rewardFail))
            return rewardFail;

        FinalizeClaim(store, comp, contractId, contract.Repeatable);

        return ClaimAttemptResult.Ok();
    }

    private bool TryValidateObjectiveClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (contract.Runtime.Failed)
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.ObjectiveFailed, contract.Runtime.FailureReason);
            return false;
        }

        if (!contract.Completed)
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Objective progress {contract.Progress}/{contract.Required} for '{contractId}'.");
            return false;
        }

        if (IsGhostRoleSelfClaim(store, contractId, user, contract))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.InvalidTarget,
                $"Contract ghost role target cannot claim its own contract '{contractId}'.");
            return false;
        }

        return TryValidateContractRewards(user, contract.Rewards, out fail);
    }

    private bool TryExecuteObjectiveClaimRewards(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        out ClaimAttemptResult fail
    )
    {
        var journal = new ObjectiveConsumeJournal();
        if (TryGiveContractRewardsWithPreCommit(
                user,
                contract.Rewards,
                () => TryPreCommitObjectiveClaim(store, user, contractId, contract, journal),
                out fail))
        {
            CommitObjectiveConsumeJournal(journal);
            return true;
        }

        RollbackObjectiveConsumeJournal(journal);
        return false;
    }

    private ClaimAttemptResult TryPreCommitObjectiveClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveConsumeJournal journal
    )
    {
        if (!TryConsumeSpawnedHuntBodyTurnIn(store, user, contractId, contract, journal, out var bodyFail))
            return bodyFail;

        if (!TryConsumeObjectiveProof(store, user, contractId, contract, journal, out var proofFail))
            return proofFail;

        TryMarkGhostRoleRoundEndClaimed(store, contractId, contract, journal);
        return ClaimAttemptResult.Ok();
    }
}
