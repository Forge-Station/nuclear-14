using System.Linq;
using Content.Shared._NC.Trade;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;


namespace Content.Client._NC.Trade;


public sealed class NcStoreStructuredBoundUi(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IPlayerManager _player = IoCManager.Resolve<IPlayerManager>();
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private int _lastHash = int.MinValue;

    private NcStoreMenu? _menu;
    private TimeSpan _nextRefreshTime = TimeSpan.Zero;

    private EntityUid? Actor => _player.LocalSession?.AttachedEntity;

    private void RequestRefresh(bool force = false)
    {
        var now = _timing.CurTime;
        if (!force && now < _nextRefreshTime)
            return;

        _nextRefreshTime = now + RefreshInterval;
        SendMessage(new RequestUiRefreshMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not StoreUiState st)
            return;

        EnsureMenuCreated();
        if (_menu == null)
            return;

        var hash = ComputeStateHash(st);
        if (hash == _lastHash)
            return;

        _lastHash = hash;

        _menu.ApplyState(st.Balance, st.Listings.ToList(), st.MassSellTotals);
        _menu.PopulateContracts(st.Contracts);
        _menu.Visible = true;
    }

    private static int ComputeStateHash(StoreUiState st)
    {
        var hash = 17;

        hash = CombineHash(hash, st.Balance.GetHashCode());

        foreach (var it in st.Listings)
        {
            hash = CombineHash(hash, it.Id?.GetHashCode() ?? 0);
            hash = CombineHash(hash, it.ProductEntity?.GetHashCode() ?? 0);
            hash = CombineHash(hash, it.Category?.GetHashCode() ?? 0);
            hash = CombineHash(hash, it.CurrencyId?.GetHashCode() ?? 0);
            hash = CombineHash(hash, it.Price.GetHashCode());
            hash = CombineHash(hash, it.Remaining.GetHashCode());
            hash = CombineHash(hash, it.Owned.GetHashCode());
            hash = CombineHash(hash, ((int) it.Mode).GetHashCode());
        }

        foreach (var kv in st.MassSellTotals.OrderBy(p => p.Key))
        {
            hash = CombineHash(hash, kv.Key.GetHashCode());
            hash = CombineHash(hash, kv.Value.GetHashCode());
        }

        foreach (var c in st.Contracts)
        {
            hash = CombineHash(hash, c.Id?.GetHashCode() ?? 0);
            hash = CombineHash(hash, c.TargetItem?.GetHashCode() ?? 0);
            hash = CombineHash(hash, c.Progress.GetHashCode());
            hash = CombineHash(hash, c.Required.GetHashCode());
            hash = CombineHash(hash, c.Reward.GetHashCode());
            hash = CombineHash(hash, c.RewardCurrency?.GetHashCode() ?? 0);
            hash = CombineHash(hash, c.RewardItem?.GetHashCode() ?? 0);
            hash = CombineHash(hash, c.RewardItemCount.GetHashCode());
            hash = CombineHash(hash, c.Difficulty?.GetHashCode() ?? 0);
            hash = CombineHash(hash, c.Completed.GetHashCode());

            foreach (var t in c.Targets)
            {
                hash = CombineHash(hash, t.TargetItem?.GetHashCode() ?? 0);
                hash = CombineHash(hash, t.Required.GetHashCode());
                hash = CombineHash(hash, t.Progress.GetHashCode());
            }

            foreach (var kv in c.RewardCurrencies.OrderBy(p => p.Key))
            {
                hash = CombineHash(hash, kv.Key.GetHashCode());
                hash = CombineHash(hash, kv.Value.GetHashCode());
            }

            foreach (var kv in c.RewardItems.OrderBy(p => p.Key))
            {
                hash = CombineHash(hash, kv.Key.GetHashCode());
                hash = CombineHash(hash, kv.Value.GetHashCode());
            }
        }


        return hash;
    }


    private static int CombineHash(int current, int value) => unchecked(current * 31 + value);

    private void EnsureMenuCreated()
    {
        if (_menu != null)
            return;

        _menu = this.CreateWindow<NcStoreMenu>();
        _lastHash = int.MinValue;

        if (EntMan.TryGetComponent(Owner, out MetaDataComponent? meta))
            _menu.Title = meta.EntityName;

        _menu.OnBuyPressed += OnBuy;
        _menu.OnSellPressed += OnSell;
        _menu.OnMassSellPulledCrate += OnMassSellPulledCrate;
        _menu.OnContractClaim += OnContractClaim;

        _menu.OnClose += () =>
        {
            _menu.Orphan();
            _menu = null;
            _lastHash = int.MinValue;
        };
    }


    private void OnBuy(StoreListingData data, int qty)
    {
        if (Actor is null)
            return;

        SendMessage(new StoreBuyListingBoundUiMessage(data.Id, qty));
        RequestRefresh(true);
    }

    private void OnSell(StoreListingData data, int qty)
    {
        if (Actor is null)
            return;

        SendMessage(new StoreSellListingBoundUiMessage(data.Id, qty));
        RequestRefresh(true);
    }

    private void OnContractClaim(string contractId)
    {
        if (Actor is null)
            return;

        SendMessage(new ClaimContractBoundMessage(contractId));
        RequestRefresh(true);
    }

    private void OnMassSellPulledCrate()
    {
        if (Actor is null)
            return;

        SendMessage(new StoreMassSellPulledCrateBoundUiMessage());
        RequestRefresh(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (_menu != null)
        {
            _menu.OnBuyPressed -= OnBuy;
            _menu.OnSellPressed -= OnSell;
            _menu.OnMassSellPulledCrate -= OnMassSellPulledCrate;
            _menu.OnContractClaim -= OnContractClaim;

            _menu.Orphan();
            _menu = null;
        }

        base.Dispose(disposing);
    }
}
