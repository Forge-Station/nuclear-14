using Content.Server._Forge.Warfront.FactionPoints;
using Content.Server.Popups;
using Content.Shared._Forge.Warfront;
using Content.Shared._Forge.Warfront.Components;
using Content.Shared._Forge.Warfront.FactionShop;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Warfront.FactionShop;

public sealed partial class FactionShopSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private FactionPointsSystem _factionPoints = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier BuySound = new SoundPathSpecifier("/Audio/Effects/kaching.ogg");

    private readonly Dictionary<WarfrontFaction, EntityUid> _stockAccounts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionShopComponent, FactionShopBuyListingMessage>(OnBuyRequest);
        SubscribeLocalEvent<FactionShopComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FactionShopStockComponent>();
        while (query.MoveNext(out _, out var stock))
        {
            if (now < stock.NextRotationTime)
                continue;

            RotateStock(stock);
            stock.NextRotationTime = now + GetRotationInterval(stock.Faction);
            RefreshAllConsoles(stock.Faction);
        }
    }

    private EntityUid EnsureStock(WarfrontFaction faction)
    {
        if (_stockAccounts.TryGetValue(faction, out var uid) && Exists(uid))
            return uid;

        var query = EntityQueryEnumerator<FactionShopStockComponent>();
        while (query.MoveNext(out var existingUid, out var existingComp))
        {
            if (existingComp.Faction != faction)
                continue;

            _stockAccounts[faction] = existingUid;
            return existingUid;
        }

        var newUid = Spawn(null, MapCoordinates.Nullspace);
        var newComp = EnsureComp<FactionShopStockComponent>(newUid);
        newComp.Faction = faction;
        _stockAccounts[faction] = newUid;
        return newUid;
    }

    private bool TryGetAnyConsole(WarfrontFaction faction, out FactionShopComponent shop)
    {
        var query = EntityQueryEnumerator<FactionShopComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Faction != faction)
                continue;

            shop = comp;
            return true;
        }

        shop = default!;
        return false;
    }

    private TimeSpan GetRotationInterval(WarfrontFaction faction)
    {
        return TryGetAnyConsole(faction, out var shop) ? shop.RotationInterval : TimeSpan.FromMinutes(1);
    }

    private void RotateStock(FactionShopStockComponent stock)
    {
        stock.AvailableListings.Clear();

        if (!TryGetAnyConsole(stock.Faction, out var shop) || shop.FullCatalog.Count == 0)
            return;

        var keys = new List<EntProtoId>(shop.FullCatalog.Keys);
        var count = Math.Min(shop.OffersPerRotation, keys.Count);
        foreach (var key in _random.GetItems(keys, count, allowDuplicates: false))
        {
            stock.AvailableListings[key] = shop.FullCatalog[key];
        }
    }

    private void OnBuyRequest(EntityUid uid, FactionShopComponent shop, FactionShopBuyListingMessage args)
    {
        var buyer = args.Actor;
        var stock = Comp<FactionShopStockComponent>(EnsureStock(shop.Faction));

        if (!stock.AvailableListings.TryGetValue(args.Product, out var cost))
            return;

        if (!TryComp<WarfrontFactionComponent>(buyer, out var buyerFaction) || buyerFaction.Faction != shop.Faction)
        {
            _popup.PopupEntity(Loc.GetString("faction-shop-not-your-faction"), uid, buyer, PopupType.Small);
            return;
        }

        if (!_factionPoints.TrySpendPoints(shop.Faction, cost))
        {
            _popup.PopupEntity(Loc.GetString("faction-shop-not-enough-points"), uid, buyer, PopupType.Small);
            return;
        }

        var product = Spawn(args.Product, Transform(buyer).Coordinates);
        _hands.PickupOrDrop(buyer, product);
        _audio.PlayEntity(BuySound, buyer, uid);

        RefreshAllConsoles(shop.Faction);
    }

    private void OnUiOpened(EntityUid uid, FactionShopComponent shop, BoundUIOpenedEvent args)
    {
        var stock = Comp<FactionShopStockComponent>(EnsureStock(shop.Faction));

        if (_timing.CurTime >= stock.NextRotationTime)
        {
            RotateStock(stock);
            stock.NextRotationTime = _timing.CurTime + shop.RotationInterval;
        }

        _uiSystem.SetUiState(uid, FactionShopUiKey.Key, BuildState(shop.Faction, stock));
    }

    private void RefreshAllConsoles(WarfrontFaction faction)
    {
        var stock = Comp<FactionShopStockComponent>(EnsureStock(faction));
        var state = BuildState(faction, stock);

        var query = EntityQueryEnumerator<FactionShopComponent>();
        while (query.MoveNext(out var uid, out var shop))
        {
            if (shop.Faction != faction || !_uiSystem.IsUiOpen(uid, FactionShopUiKey.Key))
                continue;

            _uiSystem.SetUiState(uid, FactionShopUiKey.Key, state);
        }
    }

    private FactionShopBoundUserInterfaceState BuildState(WarfrontFaction faction, FactionShopStockComponent stock)
    {
        return new FactionShopBoundUserInterfaceState
        {
            Balance = _factionPoints.GetBalance(faction),
            Faction = faction,
            AvailableListings = new Dictionary<EntProtoId, int>(stock.AvailableListings),
            NextRotationTime = stock.NextRotationTime,
        };
    }
}
