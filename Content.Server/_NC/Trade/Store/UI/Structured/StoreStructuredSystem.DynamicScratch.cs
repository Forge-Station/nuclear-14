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
        private readonly HashSet<string> _visibleIncomingScratch = new(StringComparer.Ordinal);

        public readonly List<EntityUid> DeepUserItems = new();
        public readonly List<EntityUid> DeepCrateItems = new();
        public readonly NcInventorySnapshot UserSnapshot = new();

        public TimeSpan NextDynamicAllowed = TimeSpan.Zero;
        public TimeSpan NextManualRefreshAllowed = TimeSpan.Zero;

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
        public DynamicStateBuffer GetReadBuffer() => _buffers[_activeIndex];

        public DynamicStateBuffer GetWriteBuffer() => _buffers[1 - _activeIndex];

        public bool UpdateVisibleIds(string[]? ids)
        {
            _visibleIncomingScratch.Clear();

            if (ids != null)
            {
                for (var i = 0; i < ids.Length; i++)
                {
                    var id = ids[i];
                    if (!string.IsNullOrWhiteSpace(id))
                        _visibleIncomingScratch.Add(id);
                }
            }

            if (_visibleIncomingScratch.Count == 0)
            {
                if (!_hasVisibleIds)
                    return false;
                _visibleListingIds.Clear();
                _visibleSig = 0;
                _hasVisibleIds = false;
                return true;
            }

            var sig = ComputeVisibleIdsSignature(_visibleIncomingScratch);

            if (_hasVisibleIds &&
                sig == _visibleSig &&
                _visibleListingIds.SetEquals(_visibleIncomingScratch))
                return false;

            _visibleListingIds.Clear();
            foreach (var id in _visibleIncomingScratch)
                _visibleListingIds.Add(id);

            _visibleSig = sig;
            _hasVisibleIds = true;
            return true;
        }

        private static int ComputeVisibleIdsSignature(HashSet<string> ids)
        {
            var sig = 17;
            foreach (var id in ids)
                sig = unchecked(sig + (StableStringHash(id) * 31));

            sig = unchecked(sig * 31 + ids.Count);
            return sig;
        }

        private static int StableStringHash(string value)
        {
            unchecked
            {
                const int fnvPrime = 16777619;
                var hash = unchecked((int) 2166136261u);

                for (var i = 0; i < value.Length; i++)
                    hash = (hash ^ value[i]) * fnvPrime;

                return hash;
            }
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
