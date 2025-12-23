using Content.Shared._NC.Trade;
using Robust.Client.Player;
using Robust.Client.UserInterface;


namespace Content.Client._NC.Trade;


public sealed class NcStoreStructuredBoundUi : BoundUserInterface
{
    private readonly IPlayerManager _player = IoCManager.Resolve<IPlayerManager>();

    private int _lastCatalogRevision = int.MinValue;
    private int _lastStateRevision = int.MinValue;

    private NcStoreMenu? _menu;

    private StoreDynamicState? _pendingDynamic;

    public NcStoreStructuredBoundUi(EntityUid owner, Enum uiKey)
        : base(owner, uiKey) { }

    private EntityUid? Actor => _player.LocalSession?.AttachedEntity;

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not StoreDynamicState st)
            return;

        EnsureMenuCreated();
        if (_menu == null)
            return;

        if (st.CatalogRevision != _lastCatalogRevision)
        {
            _pendingDynamic = st;
            _menu.Visible = false;
            return;
        }

        if (st.Revision == _lastStateRevision)
            return;

        _lastStateRevision = st.Revision;

        ApplyDynamic(st);
        _menu.Visible = true;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is not StoreCatalogMessage cat)
            return;

        EnsureMenuCreated();
        if (_menu == null)
            return;

        if (cat.CatalogRevision == _lastCatalogRevision)
            return;

        _lastCatalogRevision = cat.CatalogRevision;
        _lastStateRevision = int.MinValue;

        _menu.PopulateCatalog(
            cat.Listings,
            cat.HasBuyTab,
            cat.HasSellTab,
            cat.HasContractsTab);

        if (_pendingDynamic is { } pending &&
            pending.CatalogRevision == _lastCatalogRevision)
        {
            _pendingDynamic = null;
            _lastStateRevision = pending.Revision;
            ApplyDynamic(pending);
            _menu.Visible = true;
        }
        else
            _menu.Visible = false;
    }

    private void ApplyDynamic(StoreDynamicState st) =>
        _menu!.ApplyDynamicState(
            st.BalanceByCurrency,
            st.RemainingById,
            st.OwnedById,
            st.CrateUnitsById,
            st.MassSellTotals,
            st.HasBuyTab,
            st.HasSellTab,
            st.HasContractsTab,
            st.Contracts);

    private void EnsureMenuCreated()
    {
        if (_menu != null)
            return;

        _menu = this.CreateWindow<NcStoreMenu>();
        _menu.Visible = false;

        _lastCatalogRevision = int.MinValue;
        _lastStateRevision = int.MinValue;
        _pendingDynamic = null;

        if (EntMan.TryGetComponent(Owner, out MetaDataComponent? meta))
            _menu.Title = meta.EntityName;

        _menu.OnBuyPressed += OnBuy;
        _menu.OnSellPressed += OnSell;
        _menu.OnMassSellPulledCrate += OnMassSellPulledCrate;
        _menu.OnContractClaim += OnContractClaim;

        _menu.OnClose += OnMenuClosed;
    }

    private void OnMenuClosed()
    {
        if (_menu == null)
            return;

        _menu.Orphan();
        _menu = null;
        _lastCatalogRevision = int.MinValue;
        _lastStateRevision = int.MinValue;
        _pendingDynamic = null;
    }

    private void OnBuy(StoreListingData data, int qty)
    {
        if (Actor == null)
            return;

        SendMessage(new StoreBuyListingBoundUiMessage(data.Id, qty));
    }

    private void OnSell(StoreListingData data, int qty)
    {
        if (Actor == null)
            return;

        SendMessage(new StoreSellListingBoundUiMessage(data.Id, qty));
    }

    private void OnContractClaim(string contractId)
    {
        if (Actor == null)
            return;

        SendMessage(new ClaimContractBoundMessage(contractId));
    }

    private void OnMassSellPulledCrate()
    {
        if (Actor == null)
            return;

        SendMessage(new StoreMassSellPulledCrateBoundUiMessage());
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

        _lastCatalogRevision = int.MinValue;
        _lastStateRevision = int.MinValue;
        _pendingDynamic = null;

        base.Dispose(disposing);
    }
}
