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

        foreach (var e in ctx.TakePlan)
        {
            if (!EntityManager.EntityExists(e.Entity))
            {
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.ExecutionFailed,
                    $"Planned entity no longer exists: {ToPrettyString(e.Entity)}");
                return false;
            }

            if (_logic.IsProtectedFromDirectSale(e.Root, e.Entity))
            {
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.ExecutionFailed,
                    $"Planned entity is protected: {ToPrettyString(e.Entity)}");
                return false;
            }

            if (!e.IsStack)
                continue;

            if (!TryComp(e.Entity, out StackComponent? stack))
            {
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.ExecutionFailed,
                    $"Planned stack has no StackComponent: {ToPrettyString(e.Entity)}");
                return false;
            }

            var have = Math.Max(stack.Count, 0);
            if (have < e.Amount)
            {
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.ExecutionFailed,
                    $"Planned stack count mismatch: need {e.Amount}, have {have} on {ToPrettyString(e.Entity)}");
                return false;
            }
        }

        // Execute
        foreach (var e in ctx.TakePlan)
        {
            if (!EntityManager.EntityExists(e.Entity))
                continue;

            if (e.IsStack)
            {
                if (!TryComp(e.Entity, out StackComponent? stack))
                    continue;

                var have = Math.Max(stack.Count, 0);
                var left = have - e.Amount;

                _stacks.SetCount(e.Entity, left, stack);

                if (stack.Count <= 0)
                    EntityManager.DeleteEntity(e.Entity);

                continue;
            }

            EntityManager.DeleteEntity(e.Entity);
        }

        _inventory.InvalidateInventoryCache(ctx.User);
        if (ctx.Crate is { } c)
            _inventory.InvalidateInventoryCache(c);

        // Mark targets as completed.
        for (var i = 0; i < ctx.Contract.Targets.Count; i++)
        {
            var t = ctx.Contract.Targets[i];
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
                continue;

            t.Progress = t.Required;
            ctx.Contract.Targets[i] = t;
        }

        GiveContractRewards(ctx.User, ctx.Contract.Rewards);

        return true;
    }

    private void GiveContractRewards(EntityUid user, List<ContractRewardData> rewards)
    {
        foreach (var reward in rewards)
        {
            if (reward.Amount <= 0 || string.IsNullOrWhiteSpace(reward.Id))
                continue;

            switch (reward.Type)
            {
                case StoreRewardType.Currency:
                    _logic.GiveCurrency(user, reward.Id, reward.Amount);
                    break;

                case StoreRewardType.Item:
                    _logic.TrySpawnProductUnits(reward.Id, user, reward.Amount);
                    break;
            }
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
