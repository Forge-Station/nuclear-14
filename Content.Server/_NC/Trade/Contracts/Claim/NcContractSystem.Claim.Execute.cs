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

        ExecuteClaimTakePlan(ctx.TakePlan);
        InvalidateClaimExecutionCaches(ctx);

        if (!TryGiveContractRewards(ctx.User, ctx.Contract.Rewards, out fail))
            return false;

        MarkClaimTargetsCompleted(ctx.Contract);

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
            ExecuteClaimTakeEntry(entry);
    }

    private void ExecuteClaimTakeEntry(ClaimTakeEntry entry)
    {
        if (!EntityManager.EntityExists(entry.Entity))
            return;

        if (!entry.IsStack)
        {
            EntityManager.DeleteEntity(entry.Entity);
            return;
        }

        if (!TryComp(entry.Entity, out StackComponent? stack))
            return;

        var left = Math.Max(stack.Count, 0) - entry.Amount;
        _stacks.SetCount(entry.Entity, left, stack);

        if (stack.Count <= 0)
            EntityManager.DeleteEntity(entry.Entity);
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

        if (_logic.TryValidateRewardList(user, rewards, out var reason))
            return true;

        fail = CreateClaimExecutionFailure(reason);
        return false;
    }

    private bool TryGiveContractRewards(
        EntityUid user,
        IReadOnlyList<ContractRewardData>? rewards,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (_logic.TryExecuteRewardList(user, rewards, "Claim", out var reason))
            return true;

        Sawmill.Error($"[Claim] Reward execution failed after claim validation: {reason}");
        fail = CreateClaimExecutionFailure(reason);
        return false;
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
