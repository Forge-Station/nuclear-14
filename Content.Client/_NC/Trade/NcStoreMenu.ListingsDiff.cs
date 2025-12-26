namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    private const string ReadySuffix = "__ready";
    private const string CrateSuffix = "__crate";

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
            var id = it.Id;

            if (id.EndsWith(CrateSuffix, StringComparison.Ordinal))
            {
                var baseId = id.Substring(0, id.Length - CrateSuffix.Length);
                it.Owned = _crateUnitsById.GetValueOrDefault(baseId, 0);

                it.Remaining = _remainingById.GetValueOrDefault(baseId, 0);

                continue;
            }

            if (id.EndsWith(ReadySuffix, StringComparison.Ordinal))
            {
                var baseId = id.Substring(0, id.Length - ReadySuffix.Length);

                it.Owned = _ownedById.GetValueOrDefault(baseId, 0);
                it.Remaining = _remainingById.GetValueOrDefault(baseId, -1);

                continue;
            }

            it.Owned = _ownedById.GetValueOrDefault(id, 0);
            it.Remaining = _remainingById.GetValueOrDefault(id, -1);
        }
    }
}
