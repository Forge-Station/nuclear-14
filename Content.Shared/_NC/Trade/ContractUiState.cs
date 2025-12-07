using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class ContractUiState : BoundUserInterfaceState
{
    public ContractUiState(List<ContractClientData> contracts)
    {
        Contracts = contracts;
    }

    public List<ContractClientData> Contracts { get; }
}

[Serializable, NetSerializable,]
public sealed class ContractClientData
{
    public bool Completed;
    public string Description = string.Empty;

    public string Difficulty = "Easy";
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Progress;
    public int Required;

    public int Reward;
    public string RewardCurrency = string.Empty;

    public string? RewardItem;
    public int RewardItemCount;

    public string TargetItem = string.Empty;

    public ContractClientData() { }

    public ContractClientData(
        string id,
        string name,
        string targetItem,
        int required,
        int progress,
        int reward,
        string rewardCurrency,
        string? rewardItem,
        int rewardItemCount,
        string difficulty,
        bool completed,
        string description
    )
    {
        Id = id;
        Name = name;
        TargetItem = targetItem;
        Required = required;
        Progress = progress;
        Reward = reward;
        RewardCurrency = rewardCurrency;
        RewardItem = rewardItem;
        RewardItemCount = rewardItemCount;
        Difficulty = difficulty;
        Completed = completed;
        Description = description;
    }
}
