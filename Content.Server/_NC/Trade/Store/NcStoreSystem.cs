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
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed class NcStoreSystem : EntitySystem
{
    private const float MaxCrateDistance = 4f;
    private const int MaxTransactionCount = 1000;
    private static readonly TimeSpan InvalidMessageWarningInterval = TimeSpan.FromSeconds(5);
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore");
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly PopupSystem _popups = default!;
    [Dependency] private readonly StoreStructuredSystem _storeUi = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    private readonly Dictionary<string, TimeSpan> _nextInvalidListingWarningByActor = new(StringComparer.Ordinal);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NcStoreComponent, StoreBuyListingBoundUiMessage>(OnBuyRequest);
        SubscribeLocalEvent<NcStoreComponent, StoreSellListingBoundUiMessage>(OnSellRequest);
        SubscribeLocalEvent<NcStoreComponent, StoreBarterListingBoundUiMessage>(OnBarterRequest);
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

        if (aXf.MapID != bXf.MapID)
            return false;

        var aPos = _transform.GetWorldPosition(aXf);
        var bPos = _transform.GetWorldPosition(bXf);

        return Vector2.Distance(aPos, bPos) <= maxDistance;
    }

    private bool IsInUseRange(EntityUid store, EntityUid user) => IsInRange(store, user, StoreTradeLimits.StoreUseDistance);


    private bool TryValidateUse(EntityUid store, NcStoreComponent comp, EntityUid actor, out string failMessage)
    {
        failMessage = string.Empty;

        if (!CanUseStore(store, comp, actor))
        {
            failMessage = Loc.GetString("nc-store-popup-no-access");
            return false;
        }

        if (!IsInUseRange(store, actor))
        {
            failMessage = Loc.GetString("nc-store-popup-too-far");
            return false;
        }

        return true;
    }

    private void EnsureListingIndex(EntityUid store, NcStoreComponent comp)
    {
        if (comp.Listings.Count > 0 && comp.ListingIndex.Count == 0)
        {
            Sawmill.Error($"[NcStore] {ToPrettyString(store)} has listings but empty ListingIndex. Rebuilding.");
            comp.RebuildListingIndex();
        }
    }

    private bool TryGetListing(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid actor,
        StoreMode mode,
        string id,
        out NcStoreListingDef listing
    )
    {
        listing = default!;

        EnsureListingIndex(store, comp);

        if (!StoreTradeLimits.IsValidMessageId(id))
        {
            WarnInvalidListingId(actor, store, mode, id, "invalid message id");
            return false;
        }

        if (!comp.ListingIndex.TryGetValue(NcStoreComponent.MakeListingKey(mode, id), out var found))
        {
            WarnInvalidListingId(actor, store, mode, id, "unknown listing id");
            return false;
        }

        listing = found;
        return true;
    }

    private void WarnInvalidListingId(EntityUid actor, EntityUid store, StoreMode mode, string? id, string reason)
    {
        var key = $"{actor}:{mode}";
        var now = _timing.CurTime;
        if (_nextInvalidListingWarningByActor.TryGetValue(key, out var nextAllowed) && now < nextAllowed)
            return;

        _nextInvalidListingWarningByActor[key] = now + InvalidMessageWarningInterval;
        Sawmill.Warning(
            $"[NcStore] {ToPrettyString(actor)} tried to use {reason} '{StoreTradeLimits.ToLogSafeId(id)}' " +
            $"(mode={mode}) at {ToPrettyString(store)}");
    }

    private bool TryGetPulledClosedCrate(EntityUid actor, out EntityUid crate, out string failMessage)
    {
        crate = default;
        failMessage = string.Empty;

        if (_logic.TryGetPulledClosedCrate(actor, out crate))
            return true;

        if (_entMan.TryGetComponent(actor, out PullerComponent? puller) &&
            puller.Pulling is { } pulled &&
            _entMan.TryGetComponent(pulled, out EntityStorageComponent? storage) &&
            storage.Open)
        {
            failMessage = Loc.GetString("nc-store-popup-crate-open");
            return false;
        }

        failMessage = Loc.GetString("nc-store-popup-no-crate");
        return false;
    }


    private void PopupFail(EntityUid actor, string message) => _popups.PopupEntity(message, actor, actor);


    private bool TryGetLockedUiUser(EntityUid store, NcStoreComponent comp, out EntityUid user)
    {
        user = default;
        if (comp.CurrentUser is not { } cur || cur == EntityUid.Invalid)
            return false;
        if (!_ui.IsUiOpen(store, StoreUiKey.Key, cur))
            return false;

        user = cur;
        return true;
    }


    private void OnBuyRequest(EntityUid uid, NcStoreComponent comp, StoreBuyListingBoundUiMessage msg)
    {
        if (!TryGetLockedUiUser(uid, comp, out var actor))
            return;

        if (!TryValidateUse(uid, comp, actor, out var fail))
        {
            PopupFail(actor, fail);
            return;
        }

        if (!TryGetListing(uid, comp, actor, StoreMode.Buy, msg.Id, out var listing))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-invalid-listing"));
            return;
        }

        if (msg.Count <= 0)
            return;

        var count = Math.Min(msg.Count, MaxTransactionCount);
        if (!_logic.TryBuy(listing.Id, uid, comp, actor, count))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-transaction-failed"));
            return;
        }

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _storeUi.RequestDynamicRefresh(uid, comp, actor);
    }

    private void OnSellRequest(EntityUid uid, NcStoreComponent comp, StoreSellListingBoundUiMessage msg)
    {
        if (!TryGetLockedUiUser(uid, comp, out var actor))
            return;

        if (!TryValidateUse(uid, comp, actor, out var fail))
        {
            PopupFail(actor, fail);
            return;
        }

        var requestedId = msg.Id;
        var fromCrate = msg.FromCrate;

        if (!TryGetListing(uid, comp, actor, StoreMode.Sell, requestedId, out var listing))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-invalid-listing"));
            return;
        }


        if (msg.Count <= 0)
            return;

        var count = Math.Min(msg.Count, MaxTransactionCount);

        bool ok;

        if (fromCrate)
        {
            if (!TryGetPulledClosedCrate(actor, out var crate, out var crateFail))
            {
                PopupFail(actor, crateFail);
                return;
            }

            if (!IsInRange(uid, crate, MaxCrateDistance))
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
        _storeUi.RequestDynamicRefresh(uid, comp, actor);
    }


    private void OnBarterRequest(EntityUid uid, NcStoreComponent comp, StoreBarterListingBoundUiMessage msg)
    {
        if (!TryGetLockedUiUser(uid, comp, out var actor))
            return;

        if (!TryValidateUse(uid, comp, actor, out var fail))
        {
            PopupFail(actor, fail);
            return;
        }

        var requestedId = msg.Id;

        if (!TryGetListing(uid, comp, actor, StoreMode.Barter, requestedId, out var listing))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-invalid-listing"));
            return;
        }

        if (msg.Count <= 0)
            return;

        var count = Math.Min(msg.Count, MaxTransactionCount);
        if (!_logic.TryBarter(listing.Id, uid, comp, actor, count))
        {
            PopupFail(actor, Loc.GetString("nc-store-popup-transaction-failed"));
            return;
        }

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _storeUi.RequestDynamicRefresh(uid, comp, actor);
    }


    private void OnMassSellPulledCrateRequest(
        EntityUid uid,
        NcStoreComponent comp,
        StoreMassSellPulledCrateBoundUiMessage msg
    )
    {
        if (!TryGetLockedUiUser(uid, comp, out var actor))
            return;

        if (!TryValidateUse(uid, comp, actor, out var fail))
        {
            PopupFail(actor, fail);
            return;
        }

        if (!TryGetPulledClosedCrate(actor, out var crate, out var crateFail))
        {
            PopupFail(actor, crateFail);
            return;
        }

        if (!IsInRange(uid, crate, MaxCrateDistance))
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
        _storeUi.RequestDynamicRefresh(uid, comp, actor);
    }
}
