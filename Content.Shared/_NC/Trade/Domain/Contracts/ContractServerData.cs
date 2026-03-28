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

    public ContractExecutionKind ExecutionKind => ContractExecutionKinds.Resolve(ObjectiveType, EnsureConfig().TargetPrototype);
    public bool IsInventoryDelivery => ExecutionKind == ContractExecutionKind.InventoryDelivery;
    public bool IsTrackedDeliveryObjective => ExecutionKind == ContractExecutionKind.TrackedDeliveryObjective;
    public bool IsHuntObjective => ExecutionKind == ContractExecutionKind.HuntObjective;
    public bool IsRepairObjective => ExecutionKind == ContractExecutionKind.RepairObjective;
    public bool IsGhostRoleObjective => ExecutionKind == ContractExecutionKind.GhostRoleObjective;
    public bool HasInventoryDeliverySpawnSupport => IsInventoryDelivery && !string.IsNullOrWhiteSpace(EnsureConfig().DeliverySpawnPrototype);
    public bool AllowsStoreWorldTurnIn => IsInventoryDelivery && EnsureConfig().AllowStoreWorldTurnIn;
    public bool UsesWorldObjectiveRuntime => ContractExecutionKinds.UsesWorldRuntime(ExecutionKind);
    public bool UsesWorldRuntimeSupport => UsesWorldObjectiveRuntime || HasInventoryDeliverySpawnSupport;
    public bool UsesStageObjectiveProgress => ContractExecutionKinds.UsesStageProgress(ExecutionKind);

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string Difficulty { get; set; } = "Easy";
    public string Description { get; set; } = string.Empty;

    public List<ContractRewardData> Rewards { get; set; } = new();

    public bool Completed
    {
        get
        {
            var targets = EnsureTargets();

            if (UsesStageObjectiveProgress)
                return Required > 0 && Progress >= Required;

            if (targets.Count > 0)
            {
                var any = false;
                foreach (var t in targets)
                {
                    if (t.Required <= 0)
                        continue;

                    any = true;
                    if (t.Progress < t.Required)
                        return false;
                }

                return any;
            }

            return Required > 0 && Progress >= Required;
        }
    }

    private ContractObjectiveConfigData EnsureConfig()
    {
        Config ??= new();
        return Config;
    }

    private List<ContractTargetServerData> EnsureTargets()
    {
        Targets ??= new();
        for (var i = Targets.Count - 1; i >= 0; i--)
        {
            if (Targets[i] == null)
                Targets.RemoveAt(i);
        }

        return Targets;
    }
}
