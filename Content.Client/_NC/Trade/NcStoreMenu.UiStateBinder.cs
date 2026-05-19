using Content.Shared._NC.Trade;

namespace Content.Client._NC.Trade;

public sealed partial class NcStoreMenu
{
    private sealed partial class UiStateBinder
    {
        private readonly NcStoreMenu _m;

        private bool _hasLastDynamic;
        private ulong _lastCrateMembershipHash;
        private ulong _lastReadyMembershipHash;
        private readonly HashSet<string> _snapshotScopeIds = new();
        private readonly List<string> _scopedRemoveScratch = new();

        public UiStateBinder(NcStoreMenu menu)
        {
            _m = menu;
        }

        private ulong ComputeReadyMembershipHash(Dictionary<string, int> ownedById, Dictionary<string, int> remainingById)
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                var catalog = _m._catalogModel.Catalog;
                for (var i = 0; i < catalog.Count; i++)
                {
                    var s = catalog[i];
                    if (s.Mode != StoreMode.Sell)
                        continue;

                    var owned = ownedById.GetValueOrDefault(s.Id, 0);
                    if (owned <= 0)
                        continue;

                    var remaining = remainingById.GetValueOrDefault(s.Id, -1);
                    if (remaining == 0)
                        continue;

                    h = (h ^ (uint) NcStoreMenu.StableStringHash(s.Id)) * 1099511628211UL;
                }

                return h;
            }
        }

        private ulong ComputeCrateMembershipHash(Dictionary<string, int> crateUnitsById)
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                var catalog = _m._catalogModel.Catalog;
                for (var i = 0; i < catalog.Count; i++)
                {
                    var s = catalog[i];
                    if (s.Mode != StoreMode.Sell)
                        continue;

                    var take = crateUnitsById.GetValueOrDefault(s.Id, 0);
                    if (take <= 0)
                        continue;

                    h = (h ^ (uint) NcStoreMenu.StableStringHash(s.Id)) * 1099511628211UL;
                }

                return h;
            }
        }

        public void PopulateCatalog(
            List<StoreListingStaticData> listings,
            bool hasBuyTab,
            bool hasSellTab,
            bool hasBarterTab,
            bool hasContractsTab,
            StoreUiColorsData? uiColors
        )
        {
            _m.ApplyUiTheme(uiColors);

            _m._hasBuyTab = hasBuyTab;
            _m._hasSellTab = hasSellTab;
            _m._hasBarterTab = hasBarterTab;
            _m._hasContractsTab = hasContractsTab;

            _m.ApplyTabsVisibility();
            _m.UpdateHeaderVisibility();

            var filtered = new List<StoreListingStaticData>(listings.Count);

            for (var i = 0; i < listings.Count; i++)
            {
                var s = listings[i];
                if (string.IsNullOrWhiteSpace(s.Id))
                    continue;

                if (s.Mode != StoreMode.Buy && s.Mode != StoreMode.Sell && s.Mode != StoreMode.Barter)
                    continue;

                if (s.Mode != StoreMode.Barter && string.IsNullOrWhiteSpace(s.ProductEntity))
                    continue;

                if (s.Mode == StoreMode.Barter &&
                    string.IsNullOrWhiteSpace(s.ProductEntity) &&
                    s.BarterCost.Count == 0 &&
                    s.BarterReceive.Count == 0 &&
                    s.BarterReceivePools.Count == 0)
                    continue;

                filtered.Add(s);
            }

            _m._catalogModel.SetCatalog(filtered);

            var productProtos = new List<string>(filtered.Count);
            for (var i = 0; i < filtered.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(filtered[i].ProductEntity))
                    productProtos.Add(filtered[i].ProductEntity);
            }

            _m.BuyView.PrepareSearchIndex(productProtos);
            _m.SellView.PrepareSearchIndex(productProtos);
            _m.BarterView.PrepareSearchIndex(productProtos);

            _m.RebuildCategoriesFromCatalog();
            _m.RebuildItemsFromCatalogAndDynamic();
            _m.UpdateVirtualSellCategories();

            _m.BuyView.SetSearch(string.Empty);
            _m.SellView.SetSearch(string.Empty);
            _m.BarterView.SetSearch(string.Empty);
            _m.RefreshListings();
            _hasLastDynamic = false;
            _lastReadyMembershipHash = 0;
            _lastCrateMembershipHash = 0;
        }

        public void ApplyDynamicState(
            Dictionary<string, int> balancesByCurrency,
            Dictionary<string, int> remainingById,
            Dictionary<string, int> ownedById,
            Dictionary<string, int> crateUnitsById,
            Dictionary<string, int> massTotals,
            bool hasBuyTab,
            bool hasSellTab,
            bool hasBarterTab,
            bool hasContractsTab,
            List<ContractClientData> contracts,
            int contractSkipCost,
            string contractSkipCurrency,
            bool isSparseDynamicSnapshot,
            List<string> snapshotScopeIds
        )
        {
            var tabsChanged = !_hasLastDynamic ||
                hasBuyTab != _m._hasBuyTab ||
                hasSellTab != _m._hasSellTab ||
                hasBarterTab != _m._hasBarterTab ||
                hasContractsTab != _m._hasContractsTab;

            _m._hasBuyTab = hasBuyTab;
            _m._hasSellTab = hasSellTab;
            _m._hasBarterTab = hasBarterTab;
            _m._hasContractsTab = hasContractsTab;

            if (tabsChanged)
            {
                _m.ApplyTabsVisibility();
                _m.UpdateHeaderVisibility();
            }

            var balancesChanged = !DictEquals(balancesByCurrency, _m._balancesByCurrency);
            if (balancesChanged)
                _m.SetBalancesByCurrency(balancesByCurrency);

            _snapshotScopeIds.Clear();
            for (var i = 0; i < snapshotScopeIds.Count; i++)
            {
                var id = snapshotScopeIds[i];
                if (!string.IsNullOrWhiteSpace(id))
                    _snapshotScopeIds.Add(id);
            }

            var hasExplicitScope = _snapshotScopeIds.Count > 0 || isSparseDynamicSnapshot;
            var remainingChanged = hasExplicitScope
                ? !ScopedDictEquals(
                    remainingById,
                    _m._catalogModel.RemainingById,
                    _snapshotScopeIds)
                : !DictEquals(remainingById, _m._catalogModel.RemainingById);
            var ownedChanged = hasExplicitScope
                ? !ScopedDictEquals(
                    ownedById,
                    _m._catalogModel.OwnedById,
                    _snapshotScopeIds)
                : !DictEquals(ownedById, _m._catalogModel.OwnedById);
            var crateChanged = !DictEquals(crateUnitsById, _m._catalogModel.CrateUnitsById);

            if (remainingChanged)
            {
                if (hasExplicitScope)
                    ApplyScopedSnapshot(
                        remainingById,
                        _m._catalogModel.RemainingById,
                        _snapshotScopeIds);
                else
                    ApplySparseSnapshot(remainingById, _m._catalogModel.RemainingById);
            }

            if (ownedChanged)
            {
                if (hasExplicitScope)
                    ApplyScopedSnapshot(
                        ownedById,
                        _m._catalogModel.OwnedById,
                        _snapshotScopeIds);
                else
                    ApplySparseSnapshot(ownedById, _m._catalogModel.OwnedById);
            }

            if (crateChanged)
                ApplySparseSnapshot(crateUnitsById, _m._catalogModel.CrateUnitsById);

            if (!DictEquals(massTotals, _m._massSellTotals))
                _m.SetMassSellTotals(massTotals);

            var trackSkipBalance = contractSkipCost > 0 && !string.IsNullOrWhiteSpace(contractSkipCurrency);
            var currentSkipBalance = trackSkipBalance
                ? balancesByCurrency.GetValueOrDefault(contractSkipCurrency, 0)
                : 0;

            _m.PopulateContracts(contracts, contractSkipCost, contractSkipCurrency, currentSkipBalance);

            var readyMembershipHash = ComputeReadyMembershipHash(ownedById, remainingById);
            var crateMembershipHash = ComputeCrateMembershipHash(crateUnitsById);

            var membershipChanged = !_hasLastDynamic ||
                readyMembershipHash != _lastReadyMembershipHash ||
                crateMembershipHash != _lastCrateMembershipHash;

            var structureChanged = membershipChanged;
            var valuesChanged = remainingChanged || ownedChanged || crateChanged;

            if (structureChanged)
            {
                _m.RebuildItemsFromCatalogAndDynamic();
                _m.UpdateVirtualSellCategories();
                _m.RefreshListings();
            }
            else if (valuesChanged)
            {
                _m._catalogModel.UpdateItemsDynamicInPlace();
                _m.RefreshListingsDynamicOnly();
            }
            else if (balancesChanged || tabsChanged)
            {
                _m.RefreshListingsDynamicOnly();
            }

            _lastReadyMembershipHash = readyMembershipHash;
            _lastCrateMembershipHash = crateMembershipHash;
            _hasLastDynamic = true;
        }
    }
}
