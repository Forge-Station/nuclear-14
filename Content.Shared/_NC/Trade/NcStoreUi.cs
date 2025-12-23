using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public enum StoreUiKey : byte
{
    Key
}

[Serializable, NetSerializable,]
public sealed class StoreCatalogMessage(
    int catalogRevision,
    List<StoreListingStaticData> listings,
    bool hasBuyTab,
    bool hasSellTab,
    bool hasContractsTab)
    : BoundUserInterfaceMessage
{
    public int CatalogRevision { get; } = catalogRevision;
    public List<StoreListingStaticData> Listings { get; } = listings;

    public bool HasBuyTab { get; } = hasBuyTab;
    public bool HasSellTab { get; } = hasSellTab;
    public bool HasContractsTab { get; } = hasContractsTab;
}

[Serializable, NetSerializable,]
public readonly record struct StoreListingStaticData(
    string Id,
    StoreMode Mode,
    string Category,
    string ProductEntity,
    int BasePrice,
    string CurrencyId
);

[Serializable, NetSerializable,]
public sealed class StoreDynamicState(
    int revision,
    int catalogRevision,
    Dictionary<string, int> balanceByCurrency,
    Dictionary<string, int> remainingById,
    Dictionary<string, int> ownedById,
    Dictionary<string, int> crateUnitsById,
    Dictionary<string, int> massSellTotals,
    List<ContractClientData> contracts,
    bool hasBuyTab,
    bool hasSellTab,
    bool hasContractsTab)
    : BoundUserInterfaceState
{
    public int Revision { get; } = revision;
    public int CatalogRevision { get; } = catalogRevision;

    public Dictionary<string, int> BalanceByCurrency { get; } = balanceByCurrency;
    public Dictionary<string, int> RemainingById { get; } = remainingById;
    public Dictionary<string, int> OwnedById { get; } = ownedById;
    public Dictionary<string, int> CrateUnitsById { get; } = crateUnitsById;

    public Dictionary<string, int> MassSellTotals { get; } = massSellTotals;

    public List<ContractClientData> Contracts { get; } = contracts;
    public bool HasBuyTab { get; } = hasBuyTab;
    public bool HasSellTab { get; } = hasSellTab;
    public bool HasContractsTab { get; } = hasContractsTab;
}

[Serializable, NetSerializable,]
public sealed class ContractUiState(List<ContractClientData> contracts) : BoundUserInterfaceState
{
    public List<ContractClientData> Contracts { get; } = contracts;
}

[Serializable, NetSerializable,]
public sealed class ContractClientData
{
    public bool Completed;

    public string Description;
    public string Difficulty;
    public string Id;
    public string Name;
    public int Progress;
    public bool Repeatable;
    public int Required;

    public int Reward;

    public Dictionary<string, int> RewardCurrencies = new();
    public string RewardCurrency;
    public string? RewardItem;
    public int RewardItemCount;
    public Dictionary<string, int> RewardItems = new();
    public string TargetItem;

    public List<ContractTargetClientData> Targets;

    public ContractClientData()
    {
        Targets = new();
        Id = string.Empty;
        Name = string.Empty;
        TargetItem = string.Empty;
        RewardCurrency = string.Empty;
        Difficulty = "Easy";
        Description = string.Empty;

        Repeatable = true;
    }

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
        string description,
        List<ContractTargetClientData>? targets = null,
        bool repeatable = true
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
        Targets = targets ?? new();

        Repeatable = repeatable;
    }
}

[Serializable, NetSerializable,]
public sealed class StoreBuyListingBoundUiMessage(string listingId, int count) : BoundUserInterfaceMessage
{
    public string ListingId { get; } = listingId;
    public int Count { get; } = count;
}

[Serializable, NetSerializable,]
public sealed class StoreSellListingBoundUiMessage(string listingId, int count) : BoundUserInterfaceMessage
{
    public string ListingId { get; } = listingId;
    public int Count { get; } = count;
}

[Serializable, NetSerializable,]
public sealed class StoreMassSellPulledCrateBoundUiMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable,]
public sealed class RequestUiRefreshMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable,]
public sealed class ClaimContractBoundMessage(string id) : BoundUserInterfaceMessage
{
    public string ContractId { get; } = id;
}

[Serializable, NetSerializable,]
public sealed class RequestContractsRefreshMessage : BoundUserInterfaceMessage { }
