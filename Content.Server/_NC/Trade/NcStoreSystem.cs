using System.Numerics;
using Content.Server.Popups;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Movement.Pulling.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;


namespace Content.Server._NC.Trade;


public sealed class NcStoreSystem : EntitySystem
{
    private const float MaxUseDistance = 2.5f;
    private const string ReadyListingSuffix = "__ready";
    private const string CrateListingSuffix = "__crate";
    private new static readonly ISawmill Log = Logger.GetSawmill("ncstore");
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly PopupSystem _popups = default!;
    [Dependency] private readonly StoreStructuredSystem _storeUi = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NcStoreComponent, StoreBuyListingBoundUiMessage>(OnBuyRequest);
        SubscribeLocalEvent<NcStoreComponent, StoreSellListingBoundUiMessage>(OnSellRequest);
        SubscribeLocalEvent<NcStoreComponent, StoreMassSellPulledCrateBoundUiMessage>(OnMassSellPulledCrateRequest);
    }

    public bool CanUseStore(EntityUid store, NcStoreComponent comp, EntityUid user)
    {
        if (!Exists(user))
            return false;

        if (TryComp(store, out AccessReaderComponent? reader))
        {
            if (!_accessReader.IsAllowed(user, store, reader))
                return false;
        }

        return true;
    }

    private bool IsInRange(EntityUid a, EntityUid b, float maxDistance)
    {
        if (!_entMan.TryGetComponent(a, out TransformComponent? aXf))
            return false;
        if (!_entMan.TryGetComponent(b, out TransformComponent? bXf))
            return false;

        var aPos = _transform.GetWorldPosition(aXf);
        var bPos = _transform.GetWorldPosition(bXf);

        return Vector2.Distance(aPos, bPos) <= maxDistance;
    }

    private bool IsInUseRange(EntityUid store, EntityUid user) => IsInRange(store, user, MaxUseDistance);


    private void PopupFail(EntityUid actor, string message) => _popups.PopupEntity(message, actor, actor);

    private static bool TryParseSellListingId(
        string rawId,
        out string listingId,
        out bool fromCrate
    )
    {
        fromCrate = false;
        listingId = rawId;

        if (string.IsNullOrEmpty(listingId))
            return false;

        if (listingId.EndsWith(CrateListingSuffix, StringComparison.Ordinal))
        {
            fromCrate = true;
            listingId = listingId[..^CrateListingSuffix.Length];
        }

        if (listingId.EndsWith(ReadyListingSuffix, StringComparison.Ordinal))
            listingId = listingId[..^ReadyListingSuffix.Length];

        return !string.IsNullOrEmpty(listingId);
    }


    private void OnBuyRequest(EntityUid uid, NcStoreComponent comp, StoreBuyListingBoundUiMessage msg)
    {
        if (comp.CurrentUser is not { } actor)
            return;

        if (!CanUseStore(uid, comp, actor))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-no-access"));
            return;
        }

        if (!IsInUseRange(uid, actor))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-too-far"));
            return;
        }

        if (comp.Listings.Count > 0 && comp.ListingIndex.Count == 0)
        {
            Log.Error($"[NcStore] {ToPrettyString(uid)} has listings but empty ListingIndex. Rebuilding.");
            comp.RebuildListingIndex();
        }

        if (!comp.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Buy, msg.ListingId),
            out var listing))
        {
            Log.Warning(
                $"[NcStore] {ToPrettyString(actor)} tried to buy invalid listing '{msg.ListingId}' at {ToPrettyString(uid)}");
            PopupFail(actor, Loc.GetString("nc-store-popup-invalid-listing"));
            return;
        }

        var count = Math.Max(1, msg.Count);
        if (!_logic.TryBuy(listing.Id, uid, comp, actor, count))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-transaction-failed"));
            return;
        }

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _storeUi.UpdateDynamicState(uid, comp, actor);
    }

    private void OnSellRequest(EntityUid uid, NcStoreComponent comp, StoreSellListingBoundUiMessage msg)
    {
        if (comp.CurrentUser is not { } actor)
            return;

        if (!CanUseStore(uid, comp, actor))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-no-access"));
            return;
        }

        if (!IsInUseRange(uid, actor))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-too-far"));
            return;
        }

        if (!TryParseSellListingId(msg.ListingId, out var requestedId, out var fromCrate))
            return;

        if (comp.Listings.Count > 0 && comp.ListingIndex.Count == 0)
        {
            Log.Error($"[NcStore] {ToPrettyString(uid)} has listings but empty ListingIndex. Rebuilding.");
            comp.RebuildListingIndex();
        }

        if (!comp.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Sell, requestedId),
            out var listing))
        {
            Log.Warning(
                $"[NcStore] {ToPrettyString(actor)} tried to sell invalid listing '{requestedId}' (raw '{msg.ListingId}') at {ToPrettyString(uid)}");
            PopupFail(actor, Loc.GetString("nc-store-popup-invalid-listing"));
            return;
        }


        var count = Math.Max(1, msg.Count);

        bool ok;

        if (fromCrate)
        {
            if (!_logic.TryGetPulledClosedCrate(actor, out var crate))
            {
                if (_entMan.TryGetComponent(actor, out PullerComponent? puller) &&
                    puller.Pulling is { } pulled &&
                    _entMan.TryGetComponent(pulled, out EntityStorageComponent? storage) &&
                    storage.Open)
                    PopupFail(actor, Loc.GetString("nc-store-popup-crate-open"));
                else
                    PopupFail(actor, Loc.GetString("nc-store-popup-no-crate"));

                return;
            }

            const float maxCrateDistance = 4f;
            if (!IsInRange(uid, crate, maxCrateDistance))
            {
                PopupFail(actor, Loc.GetString("nc-store-popup-crate-too-far"));
                return;
            }

            ok = _logic.TrySellFromContainer(listing.Id, uid, comp, actor, crate, count);
        }
        else
            ok = _logic.TrySell(listing.Id, uid, comp, actor, count);

        if (!ok)
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-transaction-failed"));
            return;
        }

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _storeUi.UpdateDynamicState(uid, comp, actor);
    }


    private void OnMassSellPulledCrateRequest(
        EntityUid uid,
        NcStoreComponent comp,
        StoreMassSellPulledCrateBoundUiMessage msg
    )
    {
        if (comp.CurrentUser is not { } actor)
            return;

        if (!CanUseStore(uid, comp, actor))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-no-access"));
            return;
        }

        if (!IsInUseRange(uid, actor))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-too-far"));
            return;
        }

        if (!_logic.TryGetPulledClosedCrate(actor, out var crate))
        {
            if (_entMan.TryGetComponent(actor, out PullerComponent? puller) &&
                puller.Pulling is { } pulled &&
                _entMan.TryGetComponent(pulled, out EntityStorageComponent? storage) &&
                storage.Open)
                PopupFail(actor, Loc.GetString("nc-store-popup-crate-open"));
            else
                PopupFail(actor, Loc.GetString("nc-store-popup-no-crate"));

            return;
        }

        const float maxDistance = 4f;
        if (!IsInRange(uid, crate, maxDistance))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-crate-too-far"));
            return;
        }

        if (!_logic.TryMassSellFromContainer(uid, comp, actor, crate))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-transaction-failed"));
            return;
        }

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _storeUi.UpdateDynamicState(uid, comp, actor);
    }
}
