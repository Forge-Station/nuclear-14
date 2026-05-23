using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryExecuteContractEffectPlan(
        IContractEffectPlan plan,
        ContractEffectContext context,
        out string? reason
    )
    {
        reason = null;
        var prepared = false;
        var committed = false;

        try
        {
            if (!plan.TryPrepare(this, context, out reason))
                return false;

            prepared = true;
            if (!plan.TryCommit(this, context, out reason))
                return false;

            committed = true;
            return true;
        }
        catch (Exception e)
        {
            reason = $"Contract effect plan threw: {e.Message}";
            Sawmill.Error($"[Contracts] Effect plan failed for '{context.ContractId}': {e}");
            return false;
        }
        finally
        {
            if (prepared && !committed)
            {
                try
                {
                    plan.Rollback(this, context);
                }
                catch (Exception e)
                {
                    Sawmill.Error($"[Contracts] Effect plan rollback failed for '{context.ContractId}': {e}");
                }
            }
        }
    }

    /// <summary>
    ///     Extension contract for claim/pre-commit side effects. New objective mechanics should expose
    ///     destructive work through an effect plan or an existing journal instead of mutating state
    ///     directly from handler code.
    /// </summary>
    private interface IContractEffectPlan
    {
        bool TryPrepare(NcContractSystem system, ContractEffectContext context, out string? reason);
        bool TryCommit(NcContractSystem system, ContractEffectContext context, out string? reason);
        void Rollback(NcContractSystem system, ContractEffectContext context);
    }

    private readonly record struct ContractEffectContext(
        EntityUid Store,
        EntityUid User,
        string ContractId,
        ContractServerData Contract);
}
