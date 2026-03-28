using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public bool TryClaim(EntityUid store, EntityUid user, string contractId)
    {
        var res = TryClaimDetailed(store, user, contractId);
        if (!res.Success)
        {
            if (res.Reason is ClaimFailureReason.NotEnoughItems or
                ClaimFailureReason.NoValidTargets or
                ClaimFailureReason.MissingCrate or
                ClaimFailureReason.MissingProof or
                ClaimFailureReason.ObjectiveNotCompleted)
                Sawmill.Info($"[Claim] Failed ({res.Reason}) '{contractId}' on {ToPrettyString(store)}: {res.Details}");
            else
                Sawmill.Warning($"[Claim] Failed ({res.Reason}) '{contractId}' on {ToPrettyString(store)}: {res.Details}");
        }

        return res.Success;
    }

    private ClaimAttemptResult TryClaimDetailed(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
            return ClaimAttemptResult.Fail(ClaimFailureReason.StoreMissing, $"Store {ToPrettyString(store)} has no NcStoreComponent.");

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
            return ClaimAttemptResult.Fail(ClaimFailureReason.ContractMissing, $"Store {ToPrettyString(store)} has no contract '{contractId}'.");

        if (!contract.Taken)
            return ClaimAttemptResult.Fail(ClaimFailureReason.NotTaken, $"Contract '{contractId}' is not taken yet.");

        switch (contract.ExecutionKind)
        {
            case ContractExecutionKind.InventoryDelivery:
                if (!TryPrepareClaimContext(store, user, contractId, out var ctx, out var prepFail))
                    return prepFail;

                if (!TryExecuteClaimTakePlan(ctx, out var execFail))
                    return execFail;

                FinalizeClaim(ctx.Store, ctx.Comp, contractId, ctx.Contract.Repeatable);
                return ClaimAttemptResult.Ok();

            case ContractExecutionKind.TrackedDeliveryObjective:
                return TryClaimTrackedDeliveryContract(store, user, contractId, comp, contract);

            default:
                return TryClaimObjectiveContract(store, user, contractId, comp, contract);
        }
    }

    private ClaimAttemptResult TryClaimObjectiveContract(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract)
    {
        EnsureObjectiveRuntimeDefaults(contract);
        UpdateObjectiveContractProgress(store, contractId, contract);

        var runtime = EnsureContractRuntime(contract);
        if (runtime.Failed)
            return ClaimAttemptResult.Fail(ClaimFailureReason.ObjectiveFailed, runtime.FailureReason);

        if (!contract.Completed)
        {
            return ClaimAttemptResult.Fail(
                ClaimFailureReason.ObjectiveNotCompleted,
                $"Objective progress {contract.Progress}/{contract.Required} for '{contractId}'.");
        }

        if (!TryConsumeObjectiveProof(store, user, contractId, contract, out var proofFail))
            return proofFail;

        GiveContractRewards(user, contract.Rewards);
        FinalizeClaim(store, comp, contractId, contract.Repeatable);

        return ClaimAttemptResult.Ok();
    }
}
