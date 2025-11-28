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

    private int _lastHash;

    private NcStoreMenu? _menu;
    private TimeSpan _nextRefreshTime = TimeSpan.Zero;

    private EntityUid? Actor => _player.LocalSession?.AttachedEntity;
    private static uint Net(EntityUid uid) => unchecked((uint) uid.Id);

    private void RequestRefresh(bool force = false)
    {
        var now = _timing.CurTime;
        if (!force && now < _nextRefreshTime)
            return;

        _nextRefreshTime = now + RefreshInterval;
        SendMessage(new RequestUiRefreshMessage());
    }

    protected override void Open()
    {
        base.Open();
        RequestRefresh(true);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ContractUiState contractState && _menu != null)
        {
            _menu.PopulateContracts(contractState.Contracts);
            return;
        }

        if (state is not StoreUiState st)
            return;

        var hash = 17;
        hash = hash * 31 + st.Balance.GetHashCode();

        foreach (var it in st.Listings)
        {
            hash = hash * 31 + (it.Id?.GetHashCode() ?? 0);
            hash = hash * 31 + it.Price.GetHashCode();
            hash = hash * 31 + it.Remaining.GetHashCode();
            hash = hash * 31 + it.Owned.GetHashCode();
            hash = hash * 31 + ((int) it.Mode).GetHashCode();
        }

        foreach (var kv in st.MassSellTotals.OrderBy(p => p.Key))
        {
            hash = hash * 31 + kv.Key.GetHashCode();
            hash = hash * 31 + kv.Value.GetHashCode();
        }

        if (_menu != null && hash == _lastHash)
            return;

        if (_menu == null)
        {
            _menu = this.CreateWindow<NcStoreMenu>();
            _menu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

            _menu.OnBuyPressed += OnBuy;
            _menu.OnSellPressed += OnSell;
            _menu.OnMassSellPulledCrate += OnMassSellPulledCrate;

            _menu.OnContractClaim += OnContractClaim;

            _menu.OnClose += () =>
            {
                _menu.Orphan();
                _menu = null;
            };
        }

        _menu.ApplyState(st.Balance, st.Listings.ToList(), st.MassSellTotals);
        _menu.Visible = true;
        _lastHash = hash;
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
