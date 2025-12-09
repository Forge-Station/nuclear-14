using System.Linq;
using System.Numerics;
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

    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
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

    private bool IsInUseRange(EntityUid store, EntityUid user)
    {
        if (!_entMan.TryGetComponent(store, out TransformComponent? storeXf))
            return false;
        if (!_entMan.TryGetComponent(user, out TransformComponent? userXf))
            return false;

        var storePos = _transform.GetWorldPosition(storeXf);
        var userPos = _transform.GetWorldPosition(userXf);

        var dist = Vector2.Distance(storePos, userPos);
        return dist <= MaxUseDistance;
    }

    private void OnBuyRequest(EntityUid uid, NcStoreComponent comp, StoreBuyListingBoundUiMessage msg)
    {
        if (comp.CurrentUser is not { } actor)
            return;

        if (!CanUseStore(uid, comp, actor))
            return;

        if (!IsInUseRange(uid, actor))
            return;

        var listing = comp.Listings.FirstOrDefault(x => x.Id == msg.ListingId && x.Mode == StoreMode.Buy);
        if (listing == null)
            return;

        var count = Math.Max(1, msg.Count);
        if (!_logic.TryBuy(listing.Id, uid, comp, actor, count))
            return;

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _storeUi.UpdateUiState(uid, comp, actor);
    }

    private void OnSellRequest(EntityUid uid, NcStoreComponent comp, StoreSellListingBoundUiMessage msg)
    {
        if (comp.CurrentUser is not { } actor)
            return;

        if (!CanUseStore(uid, comp, actor))
            return;

        if (!IsInUseRange(uid, actor))
            return;

        var listing = comp.Listings.FirstOrDefault(x => x.Id == msg.ListingId && x.Mode == StoreMode.Sell);
        if (listing == null)
            return;

        var count = Math.Max(1, msg.Count);
        if (!_logic.TrySell(listing.Id, uid, comp, actor, count))
            return;

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _storeUi.UpdateUiState(uid, comp, actor);
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
            return;

        if (!_entMan.TryGetComponent(actor, out PullerComponent? puller) ||
            puller.Pulling is not { } crate)
            return;

        if (!_entMan.TryGetComponent(crate, out EntityStorageComponent? storage))
            return;

        if (storage.Open)
            return;

        var storeXf = _entMan.GetComponent<TransformComponent>(uid);
        var crateXf = _entMan.GetComponent<TransformComponent>(crate);

        var storePos = _transform.GetWorldPosition(storeXf);
        var cratePos = _transform.GetWorldPosition(crateXf);

        const float maxDistance = 2f;
        var dist = Vector2.Distance(storePos, cratePos);

        if (dist > maxDistance)
            return;

        if (!_logic.TryMassSellFromContainer(uid, comp, actor, crate))
            return;

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid);
        _storeUi.UpdateUiState(uid, comp, actor);
    }
}
