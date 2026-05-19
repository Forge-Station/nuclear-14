namespace Content.Shared._NC.Trade;

public static class ContractExecutionKinds
{
    public static ContractObjectiveType ToObjectiveType(ContractExecutionKind kind)
    {
        return kind switch
        {
            ContractExecutionKind.HuntObjective => ContractObjectiveType.Hunt,
            ContractExecutionKind.GhostRoleObjective => ContractObjectiveType.GhostRole,
            _ => ContractObjectiveType.Delivery
        };
    }

    public static bool UsesWorldRuntime(ContractExecutionKind kind)
    {
        return kind != ContractExecutionKind.InventoryDelivery;
    }

    public static bool UsesStageProgress(ContractExecutionKind kind)
    {
        return kind is ContractExecutionKind.HuntObjective or
            ContractExecutionKind.GhostRoleObjective;
    }
}
