using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public sealed class ContractClientData
{
    public bool Completed;
    public string Description = string.Empty;
    public string Difficulty = string.Empty;
    public ContractFlowStatus FlowStatus;
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Progress;

    public bool Repeatable;
    public bool Taken;
    public bool SupportsPinpointer;
    public ContractExecutionKind ExecutionKind = ContractExecutionKind.InventoryDelivery;
    public ContractRuntimeContextData Runtime = new();
    public int Required;
    public List<ContractRewardData> Rewards = new();

    public string TargetItem = string.Empty;
    public string TurnInItem = string.Empty;
    public List<ContractTargetClientData> Targets = new();

    public ContractClientData() { }

    public ContractClientData(
        string id,
        string name,
        string difficulty,
        string description,
        bool repeatable,
        bool taken,
        bool supportsPinpointer,
        ContractExecutionKind executionKind,
        ContractRuntimeContextData runtime,
        ContractFlowStatus flowStatus,
        bool completed,
        string targetItem,
        string turnInItem,
        int required,
        int progress,
        List<ContractTargetClientData> targets,
        List<ContractRewardData> rewards)
    {
        Id = id;
        Name = name;
        Difficulty = difficulty;
        Description = description;
        Repeatable = repeatable;
        Taken = taken;
        SupportsPinpointer = supportsPinpointer;
        ExecutionKind = executionKind;
        Runtime = runtime;
        FlowStatus = flowStatus;
        Completed = completed;
        TargetItem = targetItem;
        TurnInItem = turnInItem;
        Required = required;
        Progress = progress;
        Targets = targets;
        Rewards = rewards;
    }
}
