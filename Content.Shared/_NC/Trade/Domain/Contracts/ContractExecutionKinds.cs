namespace Content.Shared._NC.Trade;

public static class ContractExecutionKinds
{
    public static ContractExecutionKind Resolve(ContractObjectiveType objectiveType, string? targetPrototype)
    {
        return objectiveType switch
        {
            ContractObjectiveType.Delivery => string.IsNullOrWhiteSpace(targetPrototype)
                ? ContractExecutionKind.InventoryDelivery
                : ContractExecutionKind.TrackedDeliveryObjective,
            ContractObjectiveType.Hunt => ContractExecutionKind.HuntObjective,
            ContractObjectiveType.Repair => ContractExecutionKind.RepairObjective,
            ContractObjectiveType.GhostRole => ContractExecutionKind.GhostRoleObjective,
            _ => ContractExecutionKind.InventoryDelivery
        };
    }

    public static ContractObjectiveType ToObjectiveType(ContractExecutionKind kind)
    {
        return kind switch
        {
            ContractExecutionKind.HuntObjective => ContractObjectiveType.Hunt,
            ContractExecutionKind.RepairObjective => ContractObjectiveType.Repair,
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
            ContractExecutionKind.RepairObjective or
            ContractExecutionKind.GhostRoleObjective;
    }
}
