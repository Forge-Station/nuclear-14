using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly SharedStackSystem _stacks = default!;

    private bool TryExecuteClaimTakePlan(
        ClaimContext ctx,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryValidateClaimTakePlan(ctx.TakePlan, out fail))
            return false;

        if (!TryValidateContractRewards(ctx.User, ctx.Contract.Rewards, out fail))
            return false;

        if (!TryGiveContractRewardsWithPreCommit(
                ctx.User,
                ctx.Contract.Rewards,
                () => TryExecuteClaimTakePlanPreCommit(ctx),
                out fail))
            return false;

        MarkClaimTargetsCompleted(ctx.Contract);

        return true;
    }

    private bool TryExecutePartialClaimTakePlan(
        string contractId,
        ClaimContext ctx,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (ctx.TakePlan.Count == 0)
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.NotEnoughItems, $"No partial turn-in items planned for '{contractId}'.");
            return false;
        }

        if (!TryValidateClaimTakePlan(ctx.TakePlan, out fail))
            return false;

        var journal = new ClaimTakeJournal();
        try
        {
            UnregisterRetrievalSpawnedCargoTakePlan(ctx.Contract, ctx.TakePlan, journal);
            if (!TryExecuteClaimTakeEntries(ctx.TakePlan, journal, out fail))
            {
                RollbackClaimTakeJournal(journal);
                InvalidateClaimExecutionCaches(ctx);
                return false;
            }

            RecordPartialTurnInProgress(ctx.Store, contractId, ctx.Contract, ctx.TakePlan, journal);
            CommitClaimTakeJournal(journal);
        }
        catch (Exception e)
        {
            RollbackClaimTakeJournal(journal);
            Sawmill.Error($"[Claim] Partial turn-in failed unexpectedly for '{contractId}': {e}");
            InvalidateClaimExecutionCaches(ctx);
            fail = CreateClaimExecutionFailure($"Partial turn-in threw {e.GetType().Name}: {e.Message}");
            return false;
        }

        InvalidateClaimExecutionCaches(ctx);
        RefreshProgressAfterPartialTurnIn(ctx, contractId);
        RetargetContractPinpointersAfterTurnIn(ctx.Store, contractId, ctx.Contract);
        return true;
    }

    private bool TryValidateClaimTakePlan(
        List<ClaimTakeEntry> takePlan,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        foreach (var entry in takePlan)
        {
            if (!TryValidateClaimTakeEntry(entry, out fail))
                return false;
        }

        return true;
    }

    private bool TryValidateClaimTakeEntry(ClaimTakeEntry entry, out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!Exists(entry.Entity))
        {
            fail = CreateClaimExecutionFailure($"Planned entity no longer exists: {ToPrettyString(entry.Entity)}");
            return false;
        }

        if (_logic.IsProtectedFromDirectSale(entry.Root, entry.Entity))
        {
            fail = CreateClaimExecutionFailure($"Planned entity is protected: {ToPrettyString(entry.Entity)}");
            return false;
        }

        if (!entry.IsStack)
            return true;

        if (!TryComp(entry.Entity, out StackComponent? stack))
        {
            fail = CreateClaimExecutionFailure($"Planned stack has no StackComponent: {ToPrettyString(entry.Entity)}");
            return false;
        }

        var have = Math.Max(stack.Count, 0);
        if (have >= entry.Amount)
            return true;

        fail = CreateClaimExecutionFailure(
            $"Planned stack count mismatch: need {entry.Amount}, have {have} on {ToPrettyString(entry.Entity)}");
        return false;
    }

    private static ClaimAttemptResult CreateClaimExecutionFailure(string message)
    {
        return ClaimAttemptResult.Fail(ClaimFailureReason.ExecutionFailed, message);
    }

    private bool TryExecuteClaimTakeEntries(
        List<ClaimTakeEntry> takePlan,
        ClaimTakeJournal journal,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        foreach (var entry in takePlan)
        {
            if (!TryExecuteClaimTakeEntry(entry, journal, out fail))
                return false;
        }

        return true;
    }

    private bool TryExecuteClaimTakeEntry(
        ClaimTakeEntry entry,
        ClaimTakeJournal journal,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!Exists(entry.Entity))
        {
            fail = CreateClaimExecutionFailure($"Planned entity no longer exists: {ToPrettyString(entry.Entity)}");
            return false;
        }

        if (_logic.IsProtectedFromDirectSale(entry.Root, entry.Entity))
        {
            fail = CreateClaimExecutionFailure($"Planned entity is protected: {ToPrettyString(entry.Entity)}");
            return false;
        }

        if (!entry.IsStack)
        {
            journal.PendingDeletes.Add(entry.Entity);
            return true;
        }

        if (!TryComp(entry.Entity, out StackComponent? stack))
        {
            fail = CreateClaimExecutionFailure($"Planned stack has no StackComponent: {ToPrettyString(entry.Entity)}");
            return false;
        }

        var have = Math.Max(stack.Count, 0);
        if (have < entry.Amount)
        {
            fail = CreateClaimExecutionFailure(
                $"Planned stack count mismatch: need {entry.Amount}, have {have} on {ToPrettyString(entry.Entity)}");
            return false;
        }

        var left = have - entry.Amount;
        journal.TrackStack(entry.Entity, stack.Count);
        _stacks.SetCount(entry.Entity, left, stack);

        if (left <= 0)
            journal.PendingDeletes.Add(entry.Entity);

        return true;
    }

    private void InvalidateClaimExecutionCaches(ClaimContext ctx)
    {
        _inventory.InvalidateInventoryCache(ctx.User);

        if (ctx.Crate is { } crate)
            _inventory.InvalidateInventoryCache(crate);
    }

    private static void MarkClaimTargetsCompleted(ContractServerData contract)
    {
        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
                continue;

            target.Progress = target.Required;
            targets[i] = target;
        }
    }

    private bool TryValidateContractRewards(
        EntityUid user,
        IReadOnlyList<ContractRewardData>? rewards,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (Rewards.TryValidateRewardList(user, rewards, out var reason))
            return true;

        fail = CreateClaimExecutionFailure(reason);
        return false;
    }

    private bool TryGiveContractRewardsWithPreCommit(
        EntityUid user,
        IReadOnlyList<ContractRewardData>? rewards,
        Func<ClaimAttemptResult> preCommit,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);
        var preCommitFail = ClaimAttemptResult.Ok();

        if (Rewards.TryExecuteRewardListWithPreCommit(
                user,
                rewards,
                "Claim",
                () =>
                {
                    preCommitFail = preCommit();
                    return preCommitFail.Success
                        ? null
                        : $"{preCommitFail.Reason}: {preCommitFail.Details}";
                },
                out var reason))
        {
            return true;
        }

        if (!preCommitFail.Success)
        {
            fail = preCommitFail;
            return false;
        }

        Sawmill.Error($"[Claim] Reward execution failed after claim validation: {reason}");
        fail = CreateClaimExecutionFailure(reason);
        return false;
    }

    private ClaimAttemptResult TryExecuteClaimTakePlanPreCommit(ClaimContext ctx)
    {
        if (!TryValidateClaimTakePlan(ctx.TakePlan, out var fail))
            return fail;

        var journal = new ClaimTakeJournal();
        try
        {
            UnregisterRetrievalSpawnedCargoTakePlan(ctx.Contract, ctx.TakePlan, journal);
            if (!TryExecuteClaimTakeEntries(ctx.TakePlan, journal, out var executeFail))
            {
                RollbackClaimTakeJournal(journal);
                InvalidateClaimExecutionCaches(ctx);
                return executeFail;
            }

            CommitClaimTakeJournal(journal);
            InvalidateClaimExecutionCaches(ctx);
            return ClaimAttemptResult.Ok();
        }
        catch (Exception e)
        {
            RollbackClaimTakeJournal(journal);
            Sawmill.Error($"[Claim] Claim take pre-commit failed unexpectedly: {e}");
            InvalidateClaimExecutionCaches(ctx);
            return CreateClaimExecutionFailure($"Claim take pre-commit threw {e.GetType().Name}: {e.Message}");
        }
    }

    private void FinalizeClaim(
        EntityUid store,
        NcStoreComponent comp,
        string contractId,
        bool repeatable,
        bool deleteTrackedEntities = true)
    {
        CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities, deleteGuards: false);

        comp.Contracts.Remove(contractId);
        if (!repeatable)
            comp.CompletedOneTimeContracts.Add(contractId);

        RefillContractsForStore(store, comp, contractId);
    }
}
