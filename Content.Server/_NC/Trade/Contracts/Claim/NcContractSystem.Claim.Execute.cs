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
            ExecuteClaimTakePlan(ctx.TakePlan, journal);
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

        if (!EntityManager.EntityExists(entry.Entity))
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

    private void ExecuteClaimTakePlan(List<ClaimTakeEntry> takePlan)
    {
        foreach (var entry in takePlan)
            ExecuteClaimTakeEntry(entry, null);
    }

    private void ExecuteClaimTakePlan(List<ClaimTakeEntry> takePlan, ClaimTakeJournal journal)
    {
        foreach (var entry in takePlan)
            ExecuteClaimTakeEntry(entry, journal);
    }

    private void ExecuteClaimTakeEntry(ClaimTakeEntry entry, ClaimTakeJournal? journal)
    {
        if (!EntityManager.EntityExists(entry.Entity))
            return;

        if (!entry.IsStack)
        {
            if (journal != null)
                journal.PendingDeletes.Add(entry.Entity);
            else
                EntityManager.DeleteEntity(entry.Entity);
            return;
        }

        if (!TryComp(entry.Entity, out StackComponent? stack))
            return;

        var left = Math.Max(stack.Count, 0) - entry.Amount;
        journal?.TrackStack(entry.Entity, stack.Count);
        _stacks.SetCount(entry.Entity, left, stack);

        if (stack.Count <= 0)
        {
            if (journal != null)
                journal.PendingDeletes.Add(entry.Entity);
            else
                EntityManager.DeleteEntity(entry.Entity);
        }
    }

    private void CommitClaimTakeJournal(ClaimTakeJournal journal)
    {
        for (var i = 0; i < journal.PendingDeletes.Count; i++)
        {
            var ent = journal.PendingDeletes[i];
            if (EntityManager.EntityExists(ent))
                EntityManager.DeleteEntity(ent);
        }

        journal.Clear();
    }

    private void RollbackClaimTakeJournal(ClaimTakeJournal journal)
    {
        if (journal.TurnInState != null)
        {
            for (var i = journal.TurnInRestores.Count - 1; i >= 0; i--)
            {
                var restore = journal.TurnInRestores[i];
                if (restore.HadValue)
                    journal.TurnInState.TurnedInByTarget[restore.Key] = restore.PreviousValue;
                else
                    journal.TurnInState.TurnedInByTarget.Remove(restore.Key);
            }
        }

        for (var i = journal.RetrievalCargoRestores.Count - 1; i >= 0; i--)
        {
            var (cargo, key) = journal.RetrievalCargoRestores[i];
            if (EntityManager.EntityExists(cargo))
                _objectiveRuntime.ByRetrievalCargo[cargo] = key;
        }

        for (var i = journal.StackRestores.Count - 1; i >= 0; i--)
        {
            var (ent, previousCount) = journal.StackRestores[i];
            if (TryComp(ent, out StackComponent? stack))
                _stacks.SetCount(ent, previousCount, stack);
        }

        journal.Clear();
    }

    private sealed class ClaimTakeJournal
    {
        public readonly List<EntityUid> PendingDeletes = new();
        public readonly List<(EntityUid Cargo, (EntityUid Store, string ContractId) Key)> RetrievalCargoRestores = new();
        public readonly List<(EntityUid Ent, int PreviousCount)> StackRestores = new();
        public readonly List<TurnInRestore> TurnInRestores = new();
        public ObjectiveRuntimeState? TurnInState;

        public void TrackStack(EntityUid ent, int previousCount)
        {
            for (var i = 0; i < StackRestores.Count; i++)
            {
                if (StackRestores[i].Ent == ent)
                    return;
            }

            StackRestores.Add((ent, previousCount));
        }

        public void TrackRetrievalCargo(EntityUid cargo, (EntityUid Store, string ContractId) key)
        {
            for (var i = 0; i < RetrievalCargoRestores.Count; i++)
            {
                if (RetrievalCargoRestores[i].Cargo == cargo)
                    return;
            }

            RetrievalCargoRestores.Add((cargo, key));
        }

        public void TrackTurnIn(
            ObjectiveRuntimeState state,
            (string TargetItem, PrototypeMatchMode MatchMode) key)
        {
            TurnInState ??= state;

            for (var i = 0; i < TurnInRestores.Count; i++)
            {
                if (TurnInRestores[i].Key == key)
                    return;
            }

            var hadValue = state.TurnedInByTarget.TryGetValue(key, out var previousValue);
            TurnInRestores.Add(new TurnInRestore(key, hadValue, previousValue));
        }

        public void Clear()
        {
            PendingDeletes.Clear();
            RetrievalCargoRestores.Clear();
            StackRestores.Clear();
            TurnInRestores.Clear();
            TurnInState = null;
        }
    }

    private readonly record struct TurnInRestore(
        (string TargetItem, PrototypeMatchMode MatchMode) Key,
        bool HadValue,
        int PreviousValue);

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

        if (_logic.TryValidateRewardList(user, rewards, out var reason))
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

        if (_logic.TryExecuteRewardListWithPreCommit(
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

        try
        {
            UnregisterRetrievalSpawnedCargoTakePlan(ctx.Contract, ctx.TakePlan);
            ExecuteClaimTakePlan(ctx.TakePlan);
            InvalidateClaimExecutionCaches(ctx);
            return ClaimAttemptResult.Ok();
        }
        catch (Exception e)
        {
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
