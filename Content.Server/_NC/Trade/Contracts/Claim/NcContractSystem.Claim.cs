namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public bool TryClaim(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryPrepareClaimContext(store, user, contractId, out var ctx))
            return false;

        if (!TryBuildClaimExecutionBatches(ctx, out var exec))
            return false;

        if (!TryExecuteClaimBatches(ctx, exec))
            return false;

        FinalizeClaim(ctx, contractId);
        return true;
    }
}
