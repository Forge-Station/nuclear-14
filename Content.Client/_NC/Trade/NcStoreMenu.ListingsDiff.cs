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
}
