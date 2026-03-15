using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    private sealed partial class DynamicScratch
    {
        private readonly DynamicStateBuffer[] _buffers = { new(), new() };
        private readonly Dictionary<string, int> _cratePreviewTotals = new();
        private readonly Dictionary<string, int> _cratePreviewUnitsById = new();
        private readonly HashSet<string> _visibleListingIds = new();
        private int _activeIndex;
        private int _catalogRevision;
        private int _cratePreviewCatalogRevision;
        private int _cratePreviewInventoryRevision;
        private bool _hasBuyTab;
        private bool _hasCratePreview;
        private bool _hasContracts;
        private bool _hasContractsFingerprint;
        private int _contractsFingerprint;
        private bool _hasMeta;
        private bool _hasSellTab;
        private bool _hasVisibleIds;
        private int _visibleSig;
        private EntityUid? _cratePreviewRoot;
        public TimeSpan NextDynamicAllowed = TimeSpan.Zero;

        public DynamicStateBuffer GetReadBuffer() => _buffers[_activeIndex];

        public DynamicStateBuffer GetWriteBuffer() => _buffers[1 - _activeIndex];

        public bool UpdateVisibleIds(string[]? ids)
        {
            if (ids == null || ids.Length == 0)
            {
                if (!_hasVisibleIds)
                    return false;

                _visibleListingIds.Clear();
                _visibleSig = 0;
                _hasVisibleIds = false;
                return true;
            }

            var sig = 17;
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                sig = unchecked(sig * 31 + id.GetHashCode());
            }

            if (_hasVisibleIds && sig == _visibleSig && _visibleListingIds.Count == ids.Length)
            {
                var all = true;
                for (var i = 0; i < ids.Length; i++)
                {
                    var id = ids[i];
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    if (_visibleListingIds.Contains(id))
                        continue;

                    all = false;
                    break;
                }

                if (all)
                    return false;
            }

            _visibleListingIds.Clear();
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (!string.IsNullOrWhiteSpace(id))
                    _visibleListingIds.Add(id);
            }

            _visibleSig = sig;
            _hasVisibleIds = true;
            return true;
        }

        public bool ShouldSendBuyDynamicFor(string listingId)
        {
            if (!_hasVisibleIds)
                return true;

            return _visibleListingIds.Contains(listingId);
        }

        public bool ShouldRebuildContracts(int fingerprint)
        {
            if (!_hasContractsFingerprint || _contractsFingerprint != fingerprint)
            {
                _contractsFingerprint = fingerprint;
                _hasContractsFingerprint = true;
                return true;
            }

            return false;
        }

        public void ResetContractsFingerprint()
        {
            _hasContractsFingerprint = false;
            _contractsFingerprint = 0;
        }

        public bool TryPopulateCachedCratePreview(
            EntityUid crateUid,
            int catalogRevision,
            int inventoryRevision,
            DynamicStateBuffer buf)
        {
            if (!_hasCratePreview ||
                _cratePreviewRoot != crateUid ||
                _cratePreviewCatalogRevision != catalogRevision ||
                _cratePreviewInventoryRevision != inventoryRevision)
                return false;

            CopyCachedCratePreviewToBuffer(buf);
            return true;
        }

        public void CacheCratePreview(
            EntityUid crateUid,
            int catalogRevision,
            int inventoryRevision,
            NcStoreLogicSystem.MassSellPlan plan)
        {
            _cratePreviewUnitsById.Clear();
            _cratePreviewTotals.Clear();

            foreach (var (key, value) in plan.UnitsByListingId)
            {
                if (!string.IsNullOrWhiteSpace(key) && value > 0)
                    _cratePreviewUnitsById[key] = value;
            }

            foreach (var (key, value) in plan.IncomeByCurrency)
            {
                if (!string.IsNullOrWhiteSpace(key) && value > 0)
                    _cratePreviewTotals[key] = value;
            }

            _cratePreviewRoot = crateUid;
            _cratePreviewCatalogRevision = catalogRevision;
            _cratePreviewInventoryRevision = inventoryRevision;
            _hasCratePreview = true;
        }

        public void ResetCachedCratePreview()
        {
            _cratePreviewUnitsById.Clear();
            _cratePreviewTotals.Clear();
            _cratePreviewRoot = null;
            _cratePreviewCatalogRevision = 0;
            _cratePreviewInventoryRevision = 0;
            _hasCratePreview = false;
        }

        private void CopyCachedCratePreviewToBuffer(DynamicStateBuffer buf)
        {
            foreach (var (key, value) in _cratePreviewUnitsById)
                buf.CrateUnitsById[key] = value;

            foreach (var (key, value) in _cratePreviewTotals)
                buf.CrateTotals[key] = value;
        }

        public bool EqualsLast(
            DynamicStateBuffer next,
            int catalogRevision,
            bool hasBuyTab,
            bool hasSellTab,
            bool hasContracts
        )
        {
            if (!_hasMeta)
                return false;

            if (_catalogRevision != catalogRevision ||
                _hasBuyTab != hasBuyTab ||
                _hasSellTab != hasSellTab ||
                _hasContracts != hasContracts)
                return false;

            var prev = GetReadBuffer();

            return DictEquals(prev.BalancesByCurrency, next.BalancesByCurrency) &&
                DictEquals(prev.RemainingById, next.RemainingById) &&
                DictEquals(prev.OwnedById, next.OwnedById) &&
                DictEquals(prev.CrateUnitsById, next.CrateUnitsById) &&
                DictEquals(prev.CrateTotals, next.CrateTotals) &&
                ListEquals(prev.Contracts, next.Contracts) &&
                prev.ContractSkipCost == next.ContractSkipCost &&
                string.Equals(prev.ContractSkipCurrency, next.ContractSkipCurrency, StringComparison.Ordinal);
        }

        public void Commit(int catalogRevision, bool hasBuyTab, bool hasSellTab, bool hasContracts)
        {
            _activeIndex = 1 - _activeIndex;
            _catalogRevision = catalogRevision;
            _hasBuyTab = hasBuyTab;
            _hasSellTab = hasSellTab;
            _hasContracts = hasContracts;
            _hasMeta = true;
        }
    }

    private sealed class DynamicStateBuffer
    {
        public readonly Dictionary<string, int> BalancesByCurrency = new();
        public readonly List<ContractClientData> Contracts = new();
        public readonly Dictionary<string, int> CrateTotals = new();
        public readonly Dictionary<string, int> CrateUnitsById = new();
        public readonly Dictionary<string, int> OwnedById = new();
        public readonly Dictionary<string, int> RemainingById = new();
        public int ContractSkipCost;
        public string ContractSkipCurrency = string.Empty;

        public void Clear()
        {
            BalancesByCurrency.Clear();
            RemainingById.Clear();
            OwnedById.Clear();
            CrateUnitsById.Clear();
            CrateTotals.Clear();
            Contracts.Clear();
            ContractSkipCost = 0;
            ContractSkipCurrency = string.Empty;
        }
    }
}
