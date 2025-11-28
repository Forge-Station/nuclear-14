using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public sealed class StoreUiState : BoundUserInterfaceState
{
    public int Balance;
    public List<StoreListingData> Listings;
    public Dictionary<string, int> MassSellTotals;
    public List<ContractClientData> Contracts;

    public StoreUiState(
        int balance,
        List<StoreListingData> listings,
        Dictionary<string, int> massSellTotals,
        List<ContractClientData> contracts)
    {
        Balance = balance;
        Listings = listings;
        MassSellTotals = massSellTotals;
        Contracts = contracts;
    }
}

