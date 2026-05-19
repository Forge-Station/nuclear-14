using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

public sealed class ContractServerData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public List<ContractTargetServerData> Targets { get; set; } = new();

    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }

    public bool Repeatable { get; set; } = true;
    public bool Taken { get; set; }
    public ContractObjectiveType ObjectiveType { get; set; } = ContractObjectiveType.Delivery;
    public ContractRuntimeContextData Runtime { get; set; } = new();
    public ContractObjectiveConfigData Config { get; set; } = new();
    public ContractFlowStatus FlowStatus { get; set; } = ContractFlowStatus.Available;

    public ContractExecutionKind ExecutionKind { get; set; } = ContractExecutionKind.InventoryDelivery;
    public bool IsInventoryDelivery => ExecutionKind == ContractExecutionKind.InventoryDelivery;
    public bool IsTrackedDeliveryObjective => ExecutionKind == ContractExecutionKind.TrackedDeliveryObjective;
    public bool IsRetrievalRouteDelivery => ExecutionKind == ContractExecutionKind.RetrievalRouteDelivery;
    public bool IsHuntObjective => ExecutionKind == ContractExecutionKind.HuntObjective;
    public bool IsGhostRoleObjective => ExecutionKind == ContractExecutionKind.GhostRoleObjective;
    public bool HasInventoryDeliverySpawnSupport => IsInventoryDelivery && !string.IsNullOrWhiteSpace(Config.DeliverySpawnPrototype);
    public bool AllowsStoreWorldTurnIn => IsInventoryDelivery && Config.AllowStoreWorldTurnIn;
    public bool UsesWorldObjectiveRuntime => ContractExecutionKinds.UsesWorldRuntime(ExecutionKind);
    public bool UsesWorldRuntimeSupport => UsesWorldObjectiveRuntime || HasInventoryDeliverySpawnSupport;
    public bool UsesStageObjectiveProgress => ContractExecutionKinds.UsesStageProgress(ExecutionKind);

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string OfferPoolId { get; set; } = string.Empty;
    public string OfferPoolName { get; set; } = string.Empty;
    public int OfferPoolOrder { get; set; } = int.MaxValue;
    public string OfferPoolColor { get; set; } = string.Empty;

    public List<ContractRewardData> Rewards { get; set; } = new();


    public bool Completed => IsCompleted();

    public bool IsCompleted()
    {
        if (UsesStageObjectiveProgress)
            return Required > 0 && Progress >= Required;

        if (Targets.Count > 0)
        {
            var any = false;
            foreach (var t in Targets)
            {
                if (t == null || t.Required <= 0)
                    continue;

                any = true;
                if (t.Progress < t.Required)
                    return false;
            }

            if (any)
                return true;
        }

        return Required > 0 && Progress >= Required;
    }
}
