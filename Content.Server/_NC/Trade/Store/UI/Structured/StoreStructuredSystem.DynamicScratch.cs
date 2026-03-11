using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    private ContractClientData MapContractToClient(ContractServerData c)
    {
        var targets = new List<ContractTargetClientData>();

        if (c.Targets is { Count: > 0, })
        {
            foreach (var t in c.Targets)
            {
                if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
                    continue;

                targets.Add(
                    new(t.TargetItem, t.Required, t.Progress)
                    {
                        MatchMode = t.MatchMode
                    });
            }
        }
        else if (!string.IsNullOrWhiteSpace(c.TargetItem) && c.Required > 0)
        {
            targets.Add(
                new(c.TargetItem, c.Required, c.Progress)
                {
                    MatchMode = c.MatchMode
                });
        }

        var rewards = c.Rewards is { Count: > 0, }
            ? new(c.Rewards)
            : new List<ContractRewardData>();

        return new(
            c.Id,
            c.Name,
            c.Difficulty,
            c.Description,
            c.Repeatable,
            c.Completed,
            c.TargetItem,
            c.Required,
            c.Progress,
            targets,
            rewards
        );
    }

    private sealed class DynamicScratch
    {
        private readonly DynamicStateBuffer[] _buffers = { new(), new(), };
        private readonly HashSet<string> _visibleListingIds = new();
        private int _activeIndex;
        private int _catalogRevision;
        private bool _hasBuyTab;
        private bool _hasContracts;
        private bool _hasContractsFingerprint;
        private int _contractsFingerprint;
        private bool _hasMeta;
        private bool _hasSellTab;
        private bool _hasVisibleIds;
        private int _visibleSig;
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
                    if (!_visibleListingIds.Contains(id))
                    {
                        all = false;
                        break;
                    }
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

        private static bool DictEquals(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            foreach (var (k, v) in a)
                if (!b.TryGetValue(k, out var bv) || bv != v)
                    return false;

            return true;
        }

        private static bool ListEquals(List<ContractClientData> a, List<ContractClientData> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; i++)
            {
                if (!ContractEquals(a[i], b[i]))
                    return false;
            }

            return true;
        }

        private static bool ContractEquals(ContractClientData? a, ContractClientData? b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;

            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal) ||
                !string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                !string.Equals(a.Difficulty, b.Difficulty, StringComparison.Ordinal) ||
                !string.Equals(a.Description, b.Description, StringComparison.Ordinal) ||
                !string.Equals(a.TargetItem, b.TargetItem, StringComparison.Ordinal))
                return false;

            if (a.Repeatable != b.Repeatable ||
                a.Completed != b.Completed ||
                a.Required != b.Required ||
                a.Progress != b.Progress)
                return false;

            return TargetsEquals(a.Targets, b.Targets) &&
                RewardsEquals(a.Rewards, b.Rewards);
        }

        private static bool TargetsEquals(List<ContractTargetClientData>? a, List<ContractTargetClientData>? b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; i++)
            {
                var at = a[i];
                var bt = b[i];
                if (!string.Equals(at.TargetItem, bt.TargetItem, StringComparison.Ordinal) ||
                    at.Required != bt.Required ||
                    at.Progress != bt.Progress ||
                    at.MatchMode != bt.MatchMode)
                    return false;
            }

            return true;
        }

        private static bool RewardsEquals(List<ContractRewardData>? a, List<ContractRewardData>? b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; i++)
            {
                var ar = a[i];
                var br = b[i];
                if (ar.Type != br.Type ||
                    ar.Amount != br.Amount ||
                    !string.Equals(ar.Id, br.Id, StringComparison.Ordinal))
                    return false;
            }

            return true;
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
