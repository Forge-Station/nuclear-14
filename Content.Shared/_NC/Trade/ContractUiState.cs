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
    public string Id = string.Empty;
    public string Name = string.Empty;

    public string TargetItem = string.Empty;
    public int Required;
    public int Progress;

    public int Reward;
    public string RewardCurrency = string.Empty;

    public string? RewardItem;
    public int RewardItemCount;

    public string Difficulty = "Easy";
    public bool Completed;
    public string Description = string.Empty;

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
        string description)
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
