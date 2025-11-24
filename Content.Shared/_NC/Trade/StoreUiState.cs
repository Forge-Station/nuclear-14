using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public sealed class StoreUiState : BoundUserInterfaceState
{
    public int Balance;
    public List<StoreListingData> Listings;
    public Dictionary<string, int> MassSellTotals;

    public StoreUiState(
        int balance,
        List<StoreListingData> listings,
        Dictionary<string, int>? massSellTotals = null)
    {
        Balance = balance;
        Listings = listings;
        MassSellTotals = massSellTotals ?? new();
    }
}
