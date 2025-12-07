using System.Linq;
using Content.Shared._NC.Trade;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;


namespace Content.Client._NC.Trade;


public sealed class NcStoreStructuredBoundUi(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private static readonly ISawmill Log = Logger.GetSawmill("ncstore-ui");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IPlayerManager _player = IoCManager.Resolve<IPlayerManager>();
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private int _lastHash;

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

        if (state == null)
        {
            Log.Error("[NcStore/UI] UpdateState received NULL state");
            return;
        }

        EnsureMenuCreated();
        if (_menu == null)
        {
            Log.Error("[NcStore/UI] UpdateState: menu is NULL after EnsureMenuCreated");
            return;
        }

        if (state is not StoreUiState st)
        {
            Log.Warning($"[NcStore/UI] Unknown state type: {state.GetType().Name}");
            return;
        }

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

        foreach (var c in st.Contracts)
        {
            hash = hash * 31 + (c.Id?.GetHashCode() ?? 0);
            hash = hash * 31 + (c.TargetItem?.GetHashCode() ?? 0);
            hash = hash * 31 + c.Progress.GetHashCode();
            hash = hash * 31 + c.Required.GetHashCode();
            hash = hash * 31 + c.Reward.GetHashCode();
            hash = hash * 31 + (c.RewardCurrency?.GetHashCode() ?? 0);
            hash = hash * 31 + (c.RewardItem?.GetHashCode() ?? 0);
            hash = hash * 31 + c.RewardItemCount.GetHashCode();
            hash = hash * 31 + (c.Difficulty?.GetHashCode() ?? 0);
            hash = hash * 31 + c.Completed.GetHashCode();
        }

        if (hash == _lastHash)
            return;

        _menu.ApplyState(st.Balance, st.Listings.ToList(), st.MassSellTotals);
        _menu.PopulateContracts(st.Contracts);
        _menu.Visible = true;
        _lastHash = hash;
    }

    private void EnsureMenuCreated()
    {
        if (_menu != null)
            return;

        try
        {
            _menu = this.CreateWindow<NcStoreMenu>();
        }
        catch (Exception ex)
        {
            Log.Error($"[NcStore/UI] FAILED to create NcStoreMenu: {ex}");
            _menu = null;
            return;
        }

        try
        {
            var meta = EntMan.GetComponent<MetaDataComponent>(Owner);
            _menu.Title = meta.EntityName;
        }
        catch (Exception ex)
        {
            Log.Error($"[NcStore/UI] Failed to set window title: {ex}");
        }

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
