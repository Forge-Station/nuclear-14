using Content.Shared._NC.Trade;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    private void RefreshListingsDynamicOnly()
    {
        if (_disposed)
            return;

        _buyGrid.UpdateDynamicOnly(GetBalanceForCurrency);
        _sellGrid.UpdateDynamicOnly(_ => int.MaxValue);
    }

    private void UpdateItemsDynamicInPlace()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var listingId = it.ListingId;

            if (it.Flavor == StoreListingFlavor.Crate)
            {
                it.Owned = _crateUnitsById.GetValueOrDefault(listingId, 0);
                it.Remaining = _remainingById.GetValueOrDefault(listingId, -1);
                continue;
            }

            it.Owned = _ownedById.GetValueOrDefault(listingId, 0);
            it.Remaining = _remainingById.GetValueOrDefault(listingId, -1);
        }
    }
}
