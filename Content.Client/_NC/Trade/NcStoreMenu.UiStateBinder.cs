using Content.Shared._NC.Trade;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    /// <summary>
    ///     Centralized mapping layer that applies catalog/dynamic state to UI.
    ///     Keeps update ordering in one place so future optimizations are localized here.
    /// </summary>
    private sealed class UiStateBinder(NcStoreMenu menu)
    {
        public void PopulateCatalog(
            List<StoreListingStaticData> listings,
            bool hasBuyTab,
            bool hasSellTab,
            bool hasContractsTab
        )
        {
            menu._hasBuyTab = hasBuyTab;
            menu._hasSellTab = hasSellTab;
            menu._hasContractsTab = hasContractsTab;

            menu.ApplyTabsVisibility();
            menu.UpdateHeaderVisibility();

            menu._catalog.Clear();
            menu._staticById.Clear();

            foreach (var s in listings)
            {
                if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.ProductEntity))
                    continue;

                menu._catalog.Add(s);
                menu._staticById[s.Id] = s;
            }
            menu._buyGrid.PrepareSearchIndex(menu._catalog.ConvertAll(x => x.ProductEntity));
            menu._sellGrid.PrepareSearchIndex(menu._catalog.ConvertAll(x => x.ProductEntity));

            menu.RebuildCategoriesFromCatalog();

            menu._buyGrid.ResetPaging();
            menu._sellGrid.ResetPaging();
            menu.RefreshListings();
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
            menu._hasBuyTab = hasBuyTab;
            menu._hasSellTab = hasSellTab;
            menu._hasContractsTab = hasContractsTab;

            menu.ApplyTabsVisibility();
            menu.UpdateHeaderVisibility();

            menu.SetBalancesByCurrency(balancesByCurrency);

            menu._remainingById.Clear();
            foreach (var (k, v) in remainingById)
                menu._remainingById[k] = v;

            menu._ownedById.Clear();
            foreach (var (k, v) in ownedById)
                menu._ownedById[k] = v;

            menu._crateUnitsById.Clear();
            foreach (var (k, v) in crateUnitsById)
                menu._crateUnitsById[k] = v;

            menu.SetMassSellTotals(massTotals);
            menu.PopulateContracts(contracts);

            menu.RebuildItemsFromCatalogAndDynamic();
            menu.UpdateVirtualSellCategories();
            menu.RefreshListings();
        }

        public void PopulateFromRaw(List<StoreListingData> list)
        {
            menu._items.Clear();
            menu._items.AddRange(list);

            menu._buyGrid.PrepareSearchIndex(menu._items.ConvertAll(x => x.ProductEntity));
            menu._sellGrid.PrepareSearchIndex(menu._items.ConvertAll(x => x.ProductEntity));

            var ids = new HashSet<string>();
            foreach (var t in menu._items)
                ids.Add(t.Id);

            menu._buyGrid.SyncAvailableIds(ids);
            menu._sellGrid.SyncAvailableIds(ids);

            menu._buyCats.Clear();
            menu._sellCats.Clear();

            var buySet = new HashSet<string>();
            var sellSet = new HashSet<string>();

            foreach (var it in list)
            {
                if (string.IsNullOrWhiteSpace(it.Category))
                    continue;

                if (it.Mode == StoreMode.Buy)
                    buySet.Add(it.Category);
                else if (it.Mode == StoreMode.Sell)
                    sellSet.Add(it.Category);
            }

            menu._buyCats.AddRange(buySet);
            menu._sellCats.AddRange(sellSet);
            menu._buyCats.Sort(static (a, b) => string.Compare(a, b, StringComparison.CurrentCulture));
            menu._sellCats.Sort(static (a, b) => string.Compare(a, b, StringComparison.CurrentCulture));
            var hasReady = false;
            foreach (var t in menu._items)
                if (t is { Mode: StoreMode.Sell, Category: CatIdReady, })
                {
                    hasReady = true;
                    break;
                }

            if (hasReady)
            {
                menu._sellCats.Remove(CatIdReady);
                menu._sellCats.Insert(0, CatIdReady);
            }

            if (!menu._buyCats.Contains(menu._buyCat))
                menu._buyCat = string.Empty;
            if (!menu._sellCats.Contains(menu._sellCat))
                menu._sellCat = string.Empty;

            menu.BuildCategoryButtons();
            menu._buyGrid.ResetPaging();
            menu._sellGrid.ResetPaging();
            menu.RefreshListings();
        }
    }
}
