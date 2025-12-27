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
            var baseId = it.Id;

            switch (it.Flavor)
            {
                case StoreListingFlavor.Crate:
                    it.Owned = _crateUnitsById.GetValueOrDefault(baseId, 0);
                    it.Remaining = _remainingById.GetValueOrDefault(baseId, -1);
                    break;

                case StoreListingFlavor.Ready:
                    it.Owned = _ownedById.GetValueOrDefault(baseId, 0);
                    it.Remaining = _remainingById.GetValueOrDefault(baseId, -1);
                    break;

                default:
                    it.Owned = _ownedById.GetValueOrDefault(baseId, 0);
                    it.Remaining = _remainingById.GetValueOrDefault(baseId, -1);
                    break;
            }

            _items[i] = it;
        }
    }

}
