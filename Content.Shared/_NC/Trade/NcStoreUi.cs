using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable]
public enum StoreUiKey : byte
{
    Key
}


[Serializable, NetSerializable]
public sealed class StoreCatalogMessage : BoundUserInterfaceMessage
{
    public StoreCatalogMessage(
        int catalogRevision,
        List<StoreListingStaticData> listings,
        bool hasBuyTab,
        bool hasSellTab,
        bool hasContractsTab
    )
    {
        CatalogRevision = catalogRevision;
        Listings = listings;
        HasBuyTab = hasBuyTab;
        HasSellTab = hasSellTab;
        HasContractsTab = hasContractsTab;
    }

    public int CatalogRevision { get; }
    public List<StoreListingStaticData> Listings { get; }
    public bool HasBuyTab { get; }
    public bool HasSellTab { get; }
    public bool HasContractsTab { get; }
}

[Serializable, NetSerializable]
public sealed class StoreListingStaticData
{
    public StoreListingStaticData(
        string id,
        StoreMode mode,
        string category,
        string productEntity,
        int basePrice,
        string currencyId
    )
    {
        Id = id;
        Mode = mode;
        Category = category;
        ProductEntity = productEntity;
        BasePrice = basePrice;
        CurrencyId = currencyId;
    }

    public string Id { get; }
    public StoreMode Mode { get; }
    public string Category { get; }
    public string ProductEntity { get; }
    public int BasePrice { get; }
    public string CurrencyId { get; }
}



[Serializable, NetSerializable]
public sealed class StoreDynamicState : BoundUserInterfaceState
{
    public StoreDynamicState(
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
        bool hasContractsTab
    )
    {
        Revision = revision;
        CatalogRevision = catalogRevision;
        BalanceByCurrency = balanceByCurrency;
        RemainingById = remainingById;
        OwnedById = ownedById;
        CrateUnitsById = crateUnitsById;
        MassSellTotals = massSellTotals;
        Contracts = contracts;
        HasBuyTab = hasBuyTab;
        HasSellTab = hasSellTab;
        HasContractsTab = hasContractsTab;
    }

    public int Revision { get; }
    public int CatalogRevision { get; }

    public Dictionary<string, int> BalanceByCurrency { get; }
    public Dictionary<string, int> RemainingById { get; }
    public Dictionary<string, int> OwnedById { get; }
    public Dictionary<string, int> CrateUnitsById { get; }

    public Dictionary<string, int> MassSellTotals { get; }

    public List<ContractClientData> Contracts { get; }

    public bool HasBuyTab { get; }
    public bool HasSellTab { get; }
    public bool HasContractsTab { get; }
}



[Serializable, NetSerializable]
public sealed class ContractClientData
{
    public bool Completed;
    public string Description = string.Empty;
    public string Difficulty = string.Empty;
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Progress;

    public bool Repeatable;
    public int Required;
    public List<ContractRewardData> Rewards = new();

    public string TargetItem = string.Empty;

    public List<ContractTargetClientData> Targets = new();

    public ContractClientData() { }

    public ContractClientData(
        string id,
        string name,
        string difficulty,
        string description,
        bool repeatable,
        bool completed,
        string targetItem,
        int required,
        int progress,
        List<ContractTargetClientData> targets,
        List<ContractRewardData> rewards
    )
    {
        Id = id;
        Name = name;
        Difficulty = difficulty;
        Description = description;
        Repeatable = repeatable;
        Completed = completed;
        TargetItem = targetItem;
        Required = required;
        Progress = progress;
        Targets = targets;
        Rewards = rewards;
    }
}



[Serializable, NetSerializable]
public sealed class StoreBuyListingBoundUiMessage : BoundUserInterfaceMessage
{
    public StoreBuyListingBoundUiMessage(string id, int count)
    {
        Id = id;
        Count = count;
    }

    public string Id { get; }
    public int Count { get; }
}

[Serializable, NetSerializable]
public sealed class StoreSellListingBoundUiMessage : BoundUserInterfaceMessage
{
    public StoreSellListingBoundUiMessage(string id, int count)
    {
        Id = id;
        Count = count;
    }

    public string Id { get; }
    public int Count { get; }
}

[Serializable, NetSerializable]
public sealed class StoreMassSellPulledCrateBoundUiMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class ClaimContractBoundMessage : BoundUserInterfaceMessage
{
    public ClaimContractBoundMessage(string contractId)
    {
        ContractId = contractId;
    }

    public string ContractId { get; }
}

[Serializable, NetSerializable]
public sealed class RequestUiRefreshMessage : BoundUserInterfaceMessage { }


[Serializable, NetSerializable]
public sealed class RequestContractsRefreshBoundMessage : BoundUserInterfaceMessage { }
