using Content.Server.Stunnable;
using Content.Shared._Forge.Cross;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Cross;

public sealed partial class CrossServerSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedCuffableSystem _cuffs = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, EntityUid> _occupiedOverlays = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeCrossEvents();
        SubscribeRestraintEvents();
    }

    private void SubscribeCrossEvents()
    {
        SubscribeLocalEvent<CrossComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<CrossComponent, DragDropTargetEvent>(OnDragDropTarget);

        SubscribeLocalEvent<CrossComponent, CrossHangDoAfterEvent>(OnHangDoAfter);
        SubscribeLocalEvent<CrossComponent, CrossUnhangDoAfterEvent>(OnUnhangDoAfter);

        SubscribeLocalEvent<CrossComponent, BreakageEventArgs>(OnCrossBroken);
        SubscribeLocalEvent<CrossComponent, DestructionEventArgs>(OnCrossDestroyed);
        SubscribeLocalEvent<CrossComponent, MapInitEvent>(OnCrossMapInit);
        SubscribeLocalEvent<CrossComponent, MoveEvent>(OnCrossMove);
        SubscribeLocalEvent<CrossComponent, ComponentShutdown>(OnCrossShutdown);
    }

    private void SubscribeRestraintEvents()
    {
        SubscribeLocalEvent<UncuffAttemptEvent>(OnUncuffAttempt);
        SubscribeLocalEvent<HungOnCrossComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<HungOnCrossComponent, DownedEvent>(OnDowned);
    }
}

