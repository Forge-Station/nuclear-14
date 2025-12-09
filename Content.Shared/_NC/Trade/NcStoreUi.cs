using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable]
public enum StoreUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class StoreUiState(
    int balance,
    List<StoreListingData> listings,
    Dictionary<string, int> massSellTotals,
    List<ContractClientData> contracts)
    : BoundUserInterfaceState
{
    public int Balance = balance;
    public List<ContractClientData> Contracts = contracts;
    public List<StoreListingData> Listings = listings;
    public Dictionary<string, int> MassSellTotals = massSellTotals;
}

[Serializable, NetSerializable]
public sealed class ContractUiState(List<ContractClientData> contracts) : BoundUserInterfaceState
{
    public List<ContractClientData> Contracts { get; } = contracts;
}

[Serializable, NetSerializable]
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

[Serializable, NetSerializable]
public sealed class StoreBuyListingBoundUiMessage(string listingId, int count) : BoundUserInterfaceMessage
{
    public string ListingId { get; } = listingId;
    public int Count { get; } = count;
}

[Serializable, NetSerializable]
public sealed class StoreSellListingBoundUiMessage(string listingId, int count) : BoundUserInterfaceMessage
{
    public string ListingId { get; } = listingId;
    public int Count { get; } = count;
}

[Serializable, NetSerializable]
public sealed class StoreMassSellPulledCrateBoundUiMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class RequestUiRefreshMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class ClaimContractBoundMessage(string id) : BoundUserInterfaceMessage
{
    public string ContractId { get; } = id;
}

[Serializable, NetSerializable]
public sealed class RequestContractsRefreshMessage : BoundUserInterfaceMessage { }
