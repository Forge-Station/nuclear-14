using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
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
        bool hasBarterTab,
        bool hasContractsTab,
        int contractSkipCost,
        string contractSkipCurrency,
        bool isSparseDynamicSnapshot = false,
        List<string>? snapshotScopeIds = null
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
        HasBarterTab = hasBarterTab;
        HasContractsTab = hasContractsTab;
        ContractSkipCost = contractSkipCost;
        ContractSkipCurrency = contractSkipCurrency;
        IsSparseDynamicSnapshot = isSparseDynamicSnapshot;
        SnapshotScopeIds = snapshotScopeIds ?? new List<string>();
    }

    public int Revision { get; }
    public int CatalogRevision { get; }

    public Dictionary<string, int> BalanceByCurrency { get; }
    public Dictionary<string, int> RemainingById { get; }

    /// <summary>
    ///     Per-listing action capacity: player-owned item count for Sell, max affordable execution count for Barter.
    /// </summary>
    public Dictionary<string, int> OwnedById { get; }

    public Dictionary<string, int> CrateUnitsById { get; }

    public Dictionary<string, int> MassSellTotals { get; }

    public List<ContractClientData> Contracts { get; }

    public bool HasBuyTab { get; }
    public bool HasSellTab { get; }
    public bool HasBarterTab { get; }
    public bool HasContractsTab { get; }

    /// <summary>Стоимость пропуска одного контракта. 0 — пропуск отключён.</summary>
    public int ContractSkipCost { get; }

    /// <summary>Валюта для оплаты пропуска (stack type id).</summary>
    public string ContractSkipCurrency { get; }

    /// <summary>
    ///     True when listing dynamic data is intentionally scoped to visible buy listings plus always-authoritative modes.
    /// </summary>
    public bool IsSparseDynamicSnapshot { get; }

    /// <summary>
    ///     Listing ids whose dynamic values are authoritative in this snapshot. Missing values for these ids mean
    ///     zero/default.
    /// </summary>
    public List<string> SnapshotScopeIds { get; }
}
