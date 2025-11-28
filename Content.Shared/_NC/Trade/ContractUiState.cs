using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable]
public sealed class ContractUiState : BoundUserInterfaceState
{
    public List<ContractClientData> Contracts { get; }

    public ContractUiState(List<ContractClientData> contracts)
    {
        Contracts = contracts;
    }
}

[Serializable, NetSerializable]
public sealed class ContractClientData
{
    public string Id { get; }
    public string TargetItem { get; }
    public int Progress { get; }
    public int Required { get; }
    public int Reward { get; }
    public string RewardCurrency { get; }
    public string Difficulty { get; }
    public bool Completed { get; }
    public string Description { get; }

    public ContractClientData(
        string id,
        string targetItem,
        int progress,
        int required,
        int reward,
        string rewardCurrency,
        string difficulty,
        bool completed,
        string description)
    {
        Id = id;
        TargetItem = targetItem;
        Progress = progress;
        Required = required;
        Reward = reward;
        RewardCurrency = rewardCurrency;
        Difficulty = difficulty;
        Completed = completed;
        Description = description;
    }
}


