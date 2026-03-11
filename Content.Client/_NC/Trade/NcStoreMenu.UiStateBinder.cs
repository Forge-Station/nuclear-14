using Content.Shared._NC.Trade;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    private sealed class UiStateBinder
    {
        private readonly NcStoreMenu _m;

        private bool _hasLastDynamic;
        private int _lastContractsHash;
        private int _lastCrateMembershipHash;
        private int _lastReadyMembershipHash;
        private int _lastSkipCost;
        private string _lastSkipCurrency = string.Empty;
        private int _lastSkipBalance;
        private readonly HashSet<string> _buyListingIds = new();

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

        private static bool SparseDictEqualsWithPreserve(
            Dictionary<string, int> src,
            Dictionary<string, int> dst,
            HashSet<string> preserveMissingIds
        )
        {
            foreach (var (k, v) in src)
            {
                if (!dst.TryGetValue(k, out var other) || other != v)
                    return false;
            }

            foreach (var key in dst.Keys)
            {
                if (src.ContainsKey(key))
                    continue;

                if (!preserveMissingIds.Contains(key))
                    return false;
            }

            return true;
        }

        private static void ApplySparseSnapshot(Dictionary<string, int> src, Dictionary<string, int> dst)
        {
            dst.Clear();

            foreach (var (k, v) in src)
            {
                if (string.IsNullOrWhiteSpace(k))
                    continue;

                dst[k] = v;
            }
        }

        private static void ApplySparseSnapshotWithPreserve(
            Dictionary<string, int> src,
            Dictionary<string, int> dst,
            HashSet<string> preserveMissingIds
        )
        {
            var toRemove = new List<string>();

            foreach (var key in dst.Keys)
            {
                if (src.ContainsKey(key))
                    continue;

                if (!preserveMissingIds.Contains(key))
                    toRemove.Add(key);
            }

            for (var i = 0; i < toRemove.Count; i++)
                dst.Remove(toRemove[i]);

            foreach (var (k, v) in src)
            {
                if (string.IsNullOrWhiteSpace(k))
                    continue;

                dst[k] = v;
            }
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
                    h = h * 31 + (c.Name?.GetHashCode() ?? 0);
                    h = h * 31 + (c.Difficulty?.GetHashCode() ?? 0);
                    h = h * 31 + (c.Description?.GetHashCode() ?? 0);
                    h = h * 31 + (c.TargetItem?.GetHashCode() ?? 0);
                    h = h * 31 + (c.Repeatable ? 1 : 0);
                    h = h * 31 + (c.Completed ? 1 : 0);
                    h = h * 31 + c.Progress;
                    h = h * 31 + c.Required;

                    var targets = c.Targets;
                    h = h * 31 + (targets?.Count ?? 0);
                    if (targets != null)
                    {
                        for (var t = 0; t < targets.Count; t++)
                        {
                            var target = targets[t];
                            h = h * 31 + (target.TargetItem?.GetHashCode() ?? 0);
                            h = h * 31 + target.Required;
                            h = h * 31 + target.Progress;
                            h = h * 31 + (int) target.MatchMode;
                        }
                    }

                    var rewards = c.Rewards;
                    h = h * 31 + (rewards?.Count ?? 0);
                    if (rewards != null)
                    {
                        for (var r = 0; r < rewards.Count; r++)
                        {
                            var reward = rewards[r];
                            h = h * 31 + (int) reward.Type;
                            h = h * 31 + (reward.Id?.GetHashCode() ?? 0);
                            h = h * 31 + reward.Amount;
                        }
                    }
                }

                return h;
            }
        }

        private int ComputeReadyMembershipHash(Dictionary<string, int> ownedById, Dictionary<string, int> remainingById)
        {
            unchecked
            {
                var h = 17;
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
                    h = h * 31 + (s.Id?.GetHashCode() ?? 0);
                }

                return h;
            }
        }

        private int ComputeCrateMembershipHash(Dictionary<string, int> crateUnitsById)
        {
            unchecked
            {
                var h = 17;

                var catalog = _m._catalogModel.Catalog;
                for (var i = 0; i < catalog.Count; i++)
                {
                    var s = catalog[i];
                    if (s.Mode != StoreMode.Sell)
                        continue;

                    var take = crateUnitsById.GetValueOrDefault(s.Id, 0);
                    if (take <= 0)
                        continue;
                    h = h * 31 + (s.Id?.GetHashCode() ?? 0);
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

            var filtered = new List<StoreListingStaticData>(listings.Count);

            for (var i = 0; i < listings.Count; i++)
            {
                var s = listings[i];
                if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.ProductEntity))
                    continue;

                filtered.Add(s);
            }

            _m._catalogModel.SetCatalog(filtered);

            _buyListingIds.Clear();
            for (var i = 0; i < filtered.Count; i++)
            {
                var listing = filtered[i];
                if (listing.Mode == StoreMode.Buy && !string.IsNullOrWhiteSpace(listing.Id))
                    _buyListingIds.Add(listing.Id);
            }

            var productProtos = new List<string>(filtered.Count);
            for (var i = 0; i < filtered.Count; i++)
                productProtos.Add(filtered[i].ProductEntity);

            _m.BuyView.PrepareSearchIndex(productProtos);
            _m.SellView.PrepareSearchIndex(productProtos);

            _m.RebuildCategoriesFromCatalog();

            _m.BuyView.SetSearch(string.Empty);
            _m.SellView.SetSearch(string.Empty);
            _m.RefreshListings();
            _hasLastDynamic = false;
            _lastContractsHash = 0;
            _lastReadyMembershipHash = 0;
            _lastCrateMembershipHash = 0;
            _lastSkipCost = 0;
            _lastSkipCurrency = string.Empty;
            _lastSkipBalance = 0;
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
            List<ContractClientData> contracts,
            int contractSkipCost,
            string contractSkipCurrency
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

            var remainingChanged =
                !SparseDictEqualsWithPreserve(remainingById, _m._catalogModel.RemainingById, _buyListingIds);
            var ownedChanged =
                !SparseDictEqualsWithPreserve(ownedById, _m._catalogModel.OwnedById, _buyListingIds);
            var crateChanged = !DictEquals(crateUnitsById, _m._catalogModel.CrateUnitsById);

            if (remainingChanged)
                ApplySparseSnapshotWithPreserve(remainingById, _m._catalogModel.RemainingById, _buyListingIds);

            if (ownedChanged)
                ApplySparseSnapshotWithPreserve(ownedById, _m._catalogModel.OwnedById, _buyListingIds);

            if (crateChanged)
                ApplySparseSnapshot(crateUnitsById, _m._catalogModel.CrateUnitsById);
            if (!DictEquals(massTotals, _m._massSellTotals))
                _m.SetMassSellTotals(massTotals);

            var skipChanged = !_hasLastDynamic ||
                contractSkipCost != _lastSkipCost ||
                !string.Equals(contractSkipCurrency, _lastSkipCurrency, StringComparison.Ordinal);

            var trackSkipBalance = contractSkipCost > 0 && !string.IsNullOrWhiteSpace(contractSkipCurrency);
            var currentSkipBalance = trackSkipBalance
                ? balancesByCurrency.GetValueOrDefault(contractSkipCurrency, 0)
                : 0;
            var skipBalanceChanged = trackSkipBalance && (!_hasLastDynamic || currentSkipBalance != _lastSkipBalance);

            var contractsHash = ComputeContractsHash(contracts);
            if (!_hasLastDynamic || contractsHash != _lastContractsHash || skipChanged || skipBalanceChanged)
            {
                _lastContractsHash = contractsHash;
                _lastSkipCost = contractSkipCost;
                _lastSkipCurrency = contractSkipCurrency;
                _lastSkipBalance = currentSkipBalance;
                _m.PopulateContracts(contracts, contractSkipCost, contractSkipCurrency, currentSkipBalance);
            }

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
                _m.RefreshListingsDynamicOnly();

            _lastReadyMembershipHash = readyMembershipHash;
            _lastCrateMembershipHash = crateMembershipHash;

            _hasLastDynamic = true;
        }
    }
}
