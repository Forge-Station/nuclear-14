using Content.Shared._NC.Trade;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    /// <summary>
    ///     Centralized mapping layer that applies catalog/dynamic state to UI.
    ///     Keeps update ordering in one place so future optimizations are localized here.
    /// </summary>
    private sealed class UiStateBinder
    {
        private readonly NcStoreMenu _m;
        private readonly List<string> _scratchProductEntities = new();
        private bool _hasLastDynamic;
        private int _lastContractsHash;

        public UiStateBinder(NcStoreMenu menu)
        {
            _m = menu;
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

            _scratchProductEntities.Clear();
            for (var i = 0; i < _m._catalog.Count; i++)
            {
                var proto = _m._catalog[i].ProductEntity;
                if (!string.IsNullOrWhiteSpace(proto))
                    _scratchProductEntities.Add(proto);
            }

            _m._buyGrid.PrepareSearchIndex(_scratchProductEntities);
            _m._sellGrid.PrepareSearchIndex(_scratchProductEntities);

            _m.RebuildCategoriesFromCatalog();

            _m._buyGrid.ResetPaging();
            _m._sellGrid.ResetPaging();
            _m.RefreshListings();
        }

        private static bool DictEquals(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            foreach (var pair in a)
                if (!b.TryGetValue(pair.Key, out var other) || other != pair.Value)
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
            var tabsChanged = !_hasLastDynamic
                || hasBuyTab != _m._hasBuyTab
                || hasSellTab != _m._hasSellTab
                || hasContractsTab != _m._hasContractsTab;

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
                foreach (var pair in remainingById)
                    _m._remainingById[pair.Key] = pair.Value;
                listingsChanged = true;
            }

            if (!DictEquals(ownedById, _m._ownedById))
            {
                _m._ownedById.Clear();
                foreach (var pair in ownedById)
                    _m._ownedById[pair.Key] = pair.Value;
                listingsChanged = true;
            }

            if (!DictEquals(crateUnitsById, _m._crateUnitsById))
            {
                _m._crateUnitsById.Clear();
                foreach (var pair in crateUnitsById)
                    _m._crateUnitsById[pair.Key] = pair.Value;
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
                _m.RefreshListings();

            _hasLastDynamic = true;
        }

        public void PopulateFromRaw(List<StoreListingData> list)
        {
            _m._items.Clear();
            _m._items.AddRange(list);

            _scratchProductEntities.Clear();
            for (var i = 0; i < _m._items.Count; i++)
            {
                var pe = _m._items[i].ProductEntity;
                if (!string.IsNullOrWhiteSpace(pe))
                    _scratchProductEntities.Add(pe);
            }

            _m._buyGrid.PrepareSearchIndex(_scratchProductEntities);
            _m._sellGrid.PrepareSearchIndex(_scratchProductEntities);

            var ids = new HashSet<string>();
            for (var i = 0; i < _m._items.Count; i++)
                ids.Add(_m._items[i].Id);

            _m._buyGrid.SyncAvailableIds(ids);
            _m._sellGrid.SyncAvailableIds(ids);

            _m._buyCats.Clear();
            _m._sellCats.Clear();

            var buySet = new HashSet<string>();
            var sellSet = new HashSet<string>();

            for (var i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (string.IsNullOrWhiteSpace(it.Category))
                    continue;

                if (it.Mode == StoreMode.Buy)
                    buySet.Add(it.Category);
                else if (it.Mode == StoreMode.Sell)
                    sellSet.Add(it.Category);
            }

            _m._buyCats.AddRange(buySet);
            _m._sellCats.AddRange(sellSet);

            if (!_m._buyCats.Contains(_m._buyCat))
                _m._buyCat = string.Empty;
            if (!_m._sellCats.Contains(_m._sellCat))
                _m._sellCat = string.Empty;

            _m._buyCategoryBar.SetCategories(_m._buyCats, _m._buyCat);
            _m._sellCategoryBar.SetCategories(_m._sellCats, _m._sellCat);

            _m._buyGrid.ResetPaging();
            _m._sellGrid.ResetPaging();
            _m.RefreshListings();
        }
    }
}
