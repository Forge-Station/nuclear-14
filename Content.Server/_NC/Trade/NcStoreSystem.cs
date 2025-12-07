using System.Linq;
using System.Numerics;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Movement.Pulling.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed class NcStoreSystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore");

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;
    [Dependency] private readonly TransformSystem _transform = default!;


    public override void Initialize()
    {
        SubscribeLocalEvent<NcStoreComponent, StoreBuyListingBoundUiMessage>(OnBuyRequest);
        SubscribeLocalEvent<NcStoreComponent, StoreSellListingBoundUiMessage>(OnSellRequest);
        SubscribeLocalEvent<NcStoreComponent, StoreMassSellPulledCrateBoundUiMessage>(OnMassSellPulledCrateRequest);
    }

    private void OnBuyRequest(EntityUid uid, NcStoreComponent comp, StoreBuyListingBoundUiMessage msg)
    {
        if (comp.CurrentUser is not { } actor)
            return;
        if (!ValidateUiAccess(uid, actor))
            return;

        var logic = _sysMan.GetEntitySystem<NcStoreLogicSystem>();
        var listing = comp.Listings.FirstOrDefault(x => x.Id == msg.ListingId);
        if (listing == null)
            return;

        var count = Math.Max(1, msg.Count);

        var ok = listing.Mode switch
        {
            StoreMode.Buy => logic.TryBuy(listing.Id, uid, comp, actor, count),
            StoreMode.Sell => logic.TrySell(listing.Id, uid, comp, actor, count),
            _ => false
        };

        if (!ok)
            return;

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _sysMan.GetEntitySystem<StoreStructuredSystem>().UpdateUiState(uid, comp, actor);
    }

    private void OnSellRequest(EntityUid uid, NcStoreComponent comp, StoreSellListingBoundUiMessage msg)
    {
        if (comp.CurrentUser is not { } actor)
            return;
        if (!ValidateUiAccess(uid, actor))
            return;

        var logic = _sysMan.GetEntitySystem<NcStoreLogicSystem>();
        var listing = comp.Listings.FirstOrDefault(x => x.Id == msg.ListingId && x.Mode == StoreMode.Sell);
        if (listing == null)
            return;

        var count = Math.Max(1, msg.Count);
        if (!logic.TrySell(listing.Id, uid, comp, actor, count))
            return;

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid, AudioParams.Default.WithVolume(-2f));
        _sysMan.GetEntitySystem<StoreStructuredSystem>().UpdateUiState(uid, comp, actor);
    }


    private void OnMassSellPulledCrateRequest(
        EntityUid uid,
        NcStoreComponent comp,
        StoreMassSellPulledCrateBoundUiMessage msg
    )
    {
        if (comp.CurrentUser is not { } actor)
            return;

        if (!ValidateUiAccess(uid, actor))
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


        var logic = _sysMan.GetEntitySystem<NcStoreLogicSystem>();

        if (!logic.TryMassSellFromContainer(uid, comp, actor, crate))
            return;

        _audio.PlayPvs("/Audio/Effects/Cargo/ping.ogg", uid);

        _sysMan.GetEntitySystem<StoreStructuredSystem>().UpdateUiState(uid, comp, actor);
    }


    private bool ValidateUiAccess(EntityUid storeUid, EntityUid user)
    {
        if (_entMan.TryGetComponent(storeUid, out NcStoreComponent? storeComp))
        {
            if (_entMan.TryGetComponent(storeUid, out AccessReaderComponent? reader))
            {
                if (!_access.IsAllowed(user, storeUid, reader))
                {
                    Sawmill.Debug($"[UI] Нет доступа: {ToPrettyString(user)} -> {ToPrettyString(storeUid)}.");
                    return false;
                }
            }
            else if (storeComp.Access is { Count: > 0, })
            {
                var fake = new AccessReaderComponent();
                fake.AccessLists.Clear();

                foreach (var group in storeComp.Access)
                {
                    var set = new HashSet<ProtoId<AccessLevelPrototype>>();

                    foreach (var token in group)
                    {
                        if (IoCManager.Resolve<IPrototypeManager>().TryIndex<AccessLevelPrototype>(token, out _))
                        {
                            set.Add(new(token));
                            continue;
                        }

                        if (IoCManager.Resolve<IPrototypeManager>().TryIndex<AccessGroupPrototype>(token, out var grp))
                        {
                            if (set.Count > 0)
                            {
                                fake.AccessLists.Add(set);
                                set = new();
                            }

                            foreach (var lvl in grp.Tags)
                                fake.AccessLists.Add(new() { lvl, });

                            continue;
                        }

                        Sawmill.Debug(
                            $"[Access] Unknown access token '{token}' on {ToPrettyString(storeUid)}; skipping.");
                    }

                    if (set.Count > 0)
                        fake.AccessLists.Add(set);
                }

                if (fake.AccessLists.Count == 0)
                {
                    Sawmill.Warning(
                        $"[Access] All access groups invalid/empty on {ToPrettyString(storeUid)}; denying.");
                    return false;
                }

                if (!_access.IsAllowed(user, storeUid, fake)) // <— порядок аргументов исправлен
                {
                    Sawmill.Debug(
                        $"[UI] Нет доступа (fallback): {ToPrettyString(user)} -> {ToPrettyString(storeUid)}.");
                    return false;
                }
            }

            if (storeComp.CurrentUser == null || storeComp.CurrentUser != user)
            {
                Sawmill.Debug(
                    $"[UI] Store busy: {ToPrettyString(storeUid)}. Current={ToPrettyString(storeComp.CurrentUser)}, Attempt={ToPrettyString(user)}");
                return false;
            }
        }

        if (!_entMan.EntityExists(user))
            return false;

        if (!_entMan.TryGetComponent(storeUid, out TransformComponent? storeXform) ||
            !_entMan.TryGetComponent(user, out TransformComponent? userXform))
            return false;

        if (!_transform.InRange(storeXform.Coordinates, userXform.Coordinates, 3f))
        {
            Sawmill.Debug($"[UI] User too far from store: {ToPrettyString(user)} -> {ToPrettyString(storeUid)}.");
            return false;
        }

        return true;
    }
}
