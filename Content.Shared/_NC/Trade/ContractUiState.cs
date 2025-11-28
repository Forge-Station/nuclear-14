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
    public string Id;
    public string TargetItem;
    public int Progress;
    public int Required;

    public int Reward;
    public string RewardCurrency;

    // 🔥 Новое:
    public string? RewardItem;
    public int RewardItemCount;

    public string Difficulty;
    public bool Completed;
    public string Description;

    public ContractClientData(
        string id,
        string targetItem,
        int progress,
        int required,
        int reward,
        string rewardCurrency,

        string? rewardItem,
        int rewardItemCount,

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

        RewardItem = rewardItem;
        RewardItemCount = rewardItemCount;

        Difficulty = difficulty;
        Completed = completed;
        Description = description;
    }
}



