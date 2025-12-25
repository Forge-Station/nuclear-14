using Content.Shared._NC.Trade;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    private sealed class UiStateBinder
    {
        private readonly NcStoreMenu _m;

        private bool _hasLastDynamic;
        private int _lastContractsHash;

        public UiStateBinder(NcStoreMenu menu)
        {
            _m = menu;
        }

        private static bool DictEquals(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            foreach (var (k, v) in a)
                if (!b.TryGetValue(k, out var other) || other != v)
                    return false;

            return true;
        }

        private static int ComputeContractsHash(List<ContractClientData> contracts)
        {
            unchecked
            {
                var h = 17;
                for (var i = 0; i < contracts.Count; i++)
                {
                    var c = contracts[i];

                    h = h * 31 + (c.Id?.GetHashCode() ?? 0);
                    h = h * 31 + (c.Completed ? 1 : 0);
                    h = h * 31 + c.Progress;
                    h = h * 31 + c.Required;
                    h = h * 31 + (c.Difficulty?.GetHashCode() ?? 0);
                    h = h * 31 + (c.Name?.GetHashCode() ?? 0);

                    h = h * 31 + (c.Targets?.Count ?? 0);
                    h = h * 31 + (c.Rewards?.Count ?? 0);
                }

                return h;
            }
        }

        public void PopulateCatalog(
            List<StoreListingStaticData> listings,
            bool hasBuyTab,
            bool hasSellTab,
            bool hasContractsTab
        )
        {
            _m._hasBuyTab = hasBuyTab;
            _m._hasSellTab = hasSellTab;
            _m._hasContractsTab = hasContractsTab;

            _m.ApplyTabsVisibility();
            _m.UpdateHeaderVisibility();

            _m._catalog.Clear();
            _m._staticById.Clear();

            for (var i = 0; i < listings.Count; i++)
            {
                var s = listings[i];
                if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.ProductEntity))
                    continue;

                _m._catalog.Add(s);
                _m._staticById[s.Id] = s;
            }

            // Build per-prototype search index once per catalog revision.
            var productProtos = new List<string>(_m._catalog.Count);
            for (var i = 0; i < _m._catalog.Count; i++)
                productProtos.Add(_m._catalog[i].ProductEntity);

            _m._buyGrid.PrepareSearchIndex(productProtos);
            _m._sellGrid.PrepareSearchIndex(productProtos);

            _m.RebuildCategoriesFromCatalog();

            _m._buyGrid.ResetPaging();
            _m._sellGrid.ResetPaging();
            _m.RefreshListings();

            _hasLastDynamic = false;
        }

        public void ApplyDynamicState(
            Dictionary<string, int> balancesByCurrency,
            Dictionary<string, int> remainingById,
            Dictionary<string, int> ownedById,
            Dictionary<string, int> crateUnitsById,
            Dictionary<string, int> massTotals,
            bool hasBuyTab,
            bool hasSellTab,
            bool hasContractsTab,
            List<ContractClientData> contracts
        )
        {
            var tabsChanged = !_hasLastDynamic ||
                hasBuyTab != _m._hasBuyTab ||
                hasSellTab != _m._hasSellTab ||
                hasContractsTab != _m._hasContractsTab;

            _m._hasBuyTab = hasBuyTab;
            _m._hasSellTab = hasSellTab;
            _m._hasContractsTab = hasContractsTab;

            if (tabsChanged)
            {
                _m.ApplyTabsVisibility();
                _m.UpdateHeaderVisibility();
            }

            var balancesChanged = !DictEquals(balancesByCurrency, _m._balancesByCurrency);
            if (balancesChanged)
                _m.SetBalancesByCurrency(balancesByCurrency);

            var listingsChanged = false;

            if (!DictEquals(remainingById, _m._remainingById))
            {
                _m._remainingById.Clear();
                foreach (var (k, v) in remainingById)
                    _m._remainingById[k] = v;
                listingsChanged = true;
            }

            if (!DictEquals(ownedById, _m._ownedById))
            {
                _m._ownedById.Clear();
                foreach (var (k, v) in ownedById)
                    _m._ownedById[k] = v;
                listingsChanged = true;
            }

            if (!DictEquals(crateUnitsById, _m._crateUnitsById))
            {
                _m._crateUnitsById.Clear();
                foreach (var (k, v) in crateUnitsById)
                    _m._crateUnitsById[k] = v;
                listingsChanged = true;
            }

            if (!DictEquals(massTotals, _m._massSellTotals))
                _m.SetMassSellTotals(massTotals);

            var contractsHash = ComputeContractsHash(contracts);
            if (!_hasLastDynamic || contractsHash != _lastContractsHash)
            {
                _lastContractsHash = contractsHash;
                _m.PopulateContracts(contracts);
            }

            if (listingsChanged)
            {
                _m.RebuildItemsFromCatalogAndDynamic();
                _m.UpdateVirtualSellCategories();
                _m.RefreshListings();
            }
            else if (balancesChanged || tabsChanged)
                _m.RefreshListingsDynamicOnly();

            _hasLastDynamic = true;
        }
    }
}
