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
        Log.Info($"[NcStore/UI] RequestRefresh(force={force}) for Owner={Owner}");
        SendMessage(new RequestUiRefreshMessage());
    }

    protected override void Open()
    {
        Log.Info($"[NcStore/UI] Open() called for store Owner={Owner}");
        base.Open();

    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state == null)
        {
            Log.Warning("[NcStore/UI] UpdateState received NULL state");
            return;
        }

        EnsureMenuCreated();
        if (_menu == null)
        {
            Log.Error("[NcStore/UI] UpdateState: menu is NULL after EnsureMenuCreated");
            return;
        }

        Log.Info($"[NcStore/UI] UpdateState received {state.GetType().Name} for Owner={Owner}");

        if (state is not StoreUiState st)
        {
            Log.Warning($"[NcStore/UI] Unknown state type: {state.GetType().Name}");
            return;
        }

        // --- Хэш как было ---
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

        if (hash == _lastHash)
        {
            Log.Info("[NcStore/UI] Skipping UI update (hash unchanged)");
            return;
        }

        Log.Info(
            $"[NcStore/UI] ApplyState: balance={st.Balance}, listings={st.Listings.Count}, massTotals={st.MassSellTotals.Count}, contracts={st.Contracts.Count}");

        // 1) Обновляем магазин
        _menu.ApplyState(st.Balance, st.Listings.ToList(), st.MassSellTotals);

        // 2) Обновляем контракты
        _menu.PopulateContracts(st.Contracts);

        _menu.Visible = true;
        _lastHash = hash;
    }


    /// <summary>
    ///     Создаёт окно, если его ещё нет.
    /// </summary>
    private void EnsureMenuCreated()
    {
        if (_menu != null)
            return;

        Log.Info("[NcStore/UI] Creating NcStoreMenu window");

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

        Log.Info("[NcStore/UI] NcStoreMenu window created successfully");

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
            Log.Info("[NcStore/UI] NcStoreMenu closed by user, disposing window");
            _menu.Orphan();
            _menu = null;
        };
    }

    private void OnBuy(StoreListingData data, int qty)
    {
        if (Actor is null)
        {
            Log.Warning("[NcStore/UI] OnBuy called but Actor is null");
            return;
        }

        Log.Info($"[NcStore/UI] OnBuy listing={data.Id} qty={qty}");
        SendMessage(new StoreBuyListingBoundUiMessage(data.Id, qty));
        RequestRefresh(true);
    }

    private void OnSell(StoreListingData data, int qty)
    {
        if (Actor is null)
        {
            Log.Warning("[NcStore/UI] OnSell called but Actor is null");
            return;
        }

        Log.Info($"[NcStore/UI] OnSell listing={data.Id} qty={qty}");
        SendMessage(new StoreSellListingBoundUiMessage(data.Id, qty));
        RequestRefresh(true);
    }

    private void OnContractClaim(string contractId)
    {
        if (Actor is null)
        {
            Log.Warning("[NcStore/UI] OnContractClaim called but Actor is null");
            return;
        }

        Log.Info($"[NcStore/UI] OnContractClaim id={contractId}");
        SendMessage(new ClaimContractBoundMessage(contractId));
        RequestRefresh(true);
    }

    private void OnMassSellPulledCrate()
    {
        if (Actor is null)
        {
            Log.Warning("[NcStore/UI] OnMassSellPulledCrate called but Actor is null");
            return;
        }

        Log.Info("[NcStore/UI] OnMassSellPulledCrate triggered");
        SendMessage(new StoreMassSellPulledCrateBoundUiMessage());
        RequestRefresh(true);
    }

    protected override void Dispose(bool disposing)
    {
        Log.Info($"[NcStore/UI] Dispose(disposing={disposing}) for Owner={Owner}");

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
