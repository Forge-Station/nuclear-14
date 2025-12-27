using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryExecuteClaimBatches(
        ClaimContext ctx,
        Dictionary<(EntityUid Root, string ProtoId), int> exec
    )
    {
        foreach (var ((root, protoId), amount) in exec)
        {
            if (amount <= 0)
                continue;

            List<EntityUid>? items = null;
            if (root == ctx.User)
                items = ctx.UserItems;
            else if (ctx.Crate is { } crate && root == crate)
                items = ctx.CrateItems;

            if (items != null)
            {
                if (!_logic.TryTakeProductUnitsFromCachedItems(root, items, protoId, amount, PrototypeMatchMode.Exact))
                {
                    Sawmill.Error(
                        $"[Claim] Take failed for {amount}x {protoId} from {ToPrettyString(root)}. Aborting claim.");
                    return false;
                }

                continue;
            }

            if (!_logic.TryTakeProductUnitsFromRoot(root, protoId, amount, PrototypeMatchMode.Exact))
            {
                Sawmill.Error(
                    $"[Claim] Take fallback failed for {amount}x {protoId} from {ToPrettyString(root)}. Aborting claim.");
                return false;
            }
        }

        _logic.InvalidateInventoryCache(ctx.User);
        if (ctx.Crate is { } c)
            _logic.InvalidateInventoryCache(c);

        // Mark progress complete.
        for (var i = 0; i < ctx.Contract.Targets.Count; i++)
        {
            var t = ctx.Contract.Targets[i];
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
                continue;

            t.Progress = t.Required;
            ctx.Contract.Targets[i] = t;
        }

        // Payout.
        foreach (var reward in ctx.Contract.Rewards)
        {
            if (reward.Amount <= 0 || string.IsNullOrWhiteSpace(reward.Id))
                continue;

            switch (reward.Type)
            {
                case StoreRewardType.Currency:
                    _logic.GiveCurrency(ctx.User, reward.Id, reward.Amount);
                    break;
                case StoreRewardType.Item:
                    for (var i = 0; i < reward.Amount; i++)
                        _logic.TrySpawnProduct(reward.Id, ctx.User);
                    break;
            }
        }

        return true;
    }

    private void FinalizeClaim(ClaimContext ctx, string contractId)
    {
        var repeatable = ctx.Contract.Repeatable;

        ctx.Comp.Contracts.Remove(contractId);
        if (!repeatable)
            ctx.Comp.CompletedOneTimeContracts.Add(contractId);

        RefillContractsForStore(ctx.Store, ctx.Comp, contractId);
    }
}
