using System.Numerics;
using Content.Server.Stunnable;
using Content.Shared._Forge.Cross;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Shared.Containers;
using Robust.Shared.Timing;


namespace Content.Server._Forge.Cross;


public sealed class CrossServerSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly CrossSystem _crossSystem = default!;
    [Dependency] private readonly SharedCuffableSystem _cuffs = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Cross Interaction & DoAfters
        SubscribeLocalEvent<CrossComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<CrossComponent, DragDropTargetEvent>(OnDragDropTarget);
        SubscribeLocalEvent<CrossComponent, CrossHangDoAfterEvent>(OnHangDoAfter);
        SubscribeLocalEvent<CrossComponent, CrossUnhangDoAfterEvent>(OnUnhangDoAfter);

        // Cross Lifecycle
        SubscribeLocalEvent<CrossComponent, MapInitEvent>(OnCrossMapInit);
        SubscribeLocalEvent<CrossComponent, MoveEvent>(OnCrossMove);
        SubscribeLocalEvent<CrossComponent, BreakageEventArgs>(OnCrossBroken);
        SubscribeLocalEvent<CrossComponent, DestructionEventArgs>(OnCrossDestroyed);
        SubscribeLocalEvent<CrossComponent, ComponentShutdown>(OnCrossShutdown);

        // Target Restraints & State
        SubscribeLocalEvent<UncuffAttemptEvent>(OnUncuffAttempt);
        SubscribeLocalEvent<HungOnCrossComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<HungOnCrossComponent, DownedEvent>(OnDowned);
    }

    #region Interaction & Actions

    private void OnInteractHand(Entity<CrossComponent> cross, ref InteractHandEvent args)
    {
        if (args.Handled || !_pulling.TryGetPulledEntity(args.User, out var target))
            return;

        args.Handled = true;
        _ = TryStartHangAction(cross, args.User, target.Value, true);
    }

    private void OnDragDropTarget(Entity<CrossComponent> cross, ref DragDropTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartHangAction(cross, args.User, args.Dragged, false);
    }

    private bool TryStartHangAction(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!CanHangNow(cross, user, target, popup))
            return false;

        if (!TryBeginAction(cross, CrossActionState.HangPending, user, target, cross.Comp.HangDelay, popup))
            return false;

        var ev = new CrossHangDoAfterEvent { HangTarget = GetNetEntity(target), ActionId = cross.Comp.ActionId, };
        if (!TryStartCrossDoAfter(cross, user, cross.Comp.HangDelay, ev))
        {
            ClearAction(cross);
            return false;
        }

        if (popup)
            _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-start", ("target", target)), cross.Owner, user);

        return true;
    }

    private bool TryStartUnhangAction(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (target == user)
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("n14-cross-popup-self-unhang-denied"), cross.Owner, user);
            return false;
        }

        if (!CanUnhangNow(cross, user, target, popup))
            return false;

        if (!TryBeginAction(cross, CrossActionState.UnhangPending, user, target, cross.Comp.UnhangDelay, popup))
            return false;

        var ev = new CrossUnhangDoAfterEvent { UnhangTarget = GetNetEntity(target), ActionId = cross.Comp.ActionId, };
        if (!TryStartCrossDoAfter(cross, user, cross.Comp.UnhangDelay, ev))
        {
            ClearAction(cross);
            return false;
        }

        if (popup)
            _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-start", ("target", target)), cross.Owner, user);

        return true;
    }

    #endregion

    #region Validation

    private bool CanHangNow(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!Exists(user) || !Exists(target) || TerminatingOrDeleted(target) || TerminatingOrDeleted(cross.Owner))
            return false;

        if (target == cross.Owner)
            return false;

        if (TryComp<HungOnCrossComponent>(target, out var hung) && hung.Cross != null)
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-fail", ("target", target)), cross.Owner, user);
            return false;
        }

        if (!TryComp<HandsComponent>(target, out var hands) || hands.Count == 0)
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-fail", ("target", target)), cross.Owner, user);
            return false;
        }

        if (cross.Comp.HungTarget is { } current && current != target)
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("n14-cross-popup-busy"), cross.Owner, user);
            return false;
        }

        if (!_container.IsInSameOrNoContainer((target, null, null), (cross.Owner, null, null)))
            return false;

        if (!_interaction.InRangeUnobstructed(user, cross.Owner, popup: false))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-fail", ("target", target)), cross.Owner, user);
            return false;
        }

        var inRange = _interaction.InRangeUnobstructed(
            target,
            cross.Owner,
            predicate: e => e == user || e == target || e == cross.Owner,
            popup: false);

        if (!inRange && popup)
            _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-fail", ("target", target)), cross.Owner, user);

        return inRange;
    }

    private bool CanUnhangNow(Entity<CrossComponent> cross, EntityUid user, EntityUid target, bool popup)
    {
        if (!Exists(user) || !Exists(target) || TerminatingOrDeleted(cross.Owner))
            return false;

        if (cross.Comp.HungTarget != target ||
            !TryComp<HungOnCrossComponent>(target, out var hung) ||
            hung.Cross != cross.Owner)
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-fail", ("target", target)), cross.Owner, user);
            return false;
        }

        var inRange = _interaction.InRangeUnobstructed(user, cross.Owner, popup: false);

        if (!inRange && popup)
            _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-fail", ("target", target)), cross.Owner, user);

        return inRange;
    }

    #endregion

    #region DoAfter Handlers

    private void OnHangDoAfter(Entity<CrossComponent> cross, ref CrossHangDoAfterEvent args)
    {
        var user = args.Args.User;
        var target = GetEntity(args.HangTarget);

        if (!TryPrepareDoAfter(
            cross,
            CrossActionState.HangPending,
            args.ActionId,
            user,
            target,
            args.Handled,
            args.Cancelled,
            "n14-cross-popup-hang-interrupted"))
            return;

        if (!CanHangNow(cross, user, target, false) || !TryHangTarget(cross, target, user))
        {
            _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-fail", ("target", target)), cross.Owner, user);
            return;
        }

        _popup.PopupEntity(Loc.GetString("n14-cross-popup-hang-success-user", ("target", target)), cross.Owner, user);
        if (target != user)
        {
            _popup.PopupEntity(
                Loc.GetString("n14-cross-popup-hang-success-target", ("user", user)),
                cross.Owner,
                target);
        }

        args.Handled = true;
    }

    private void OnUnhangDoAfter(Entity<CrossComponent> cross, ref CrossUnhangDoAfterEvent args)
    {
        var user = args.Args.User;
        var target = GetEntity(args.UnhangTarget);

        if (!TryPrepareDoAfter(
            cross,
            CrossActionState.UnhangPending,
            args.ActionId,
            user,
            target,
            args.Handled,
            args.Cancelled,
            "n14-cross-popup-unhang-interrupted"))
            return;

        if (!CanUnhangNow(cross, user, target, false) || !TryUnhangTarget(cross, target, false))
        {
            _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-fail", ("target", target)), cross.Owner, user);
            return;
        }

        _popup.PopupEntity(Loc.GetString("n14-cross-popup-unhang-success", ("target", target)), cross.Owner, user);
        args.Handled = true;
    }

    private bool TryPrepareDoAfter(
        Entity<CrossComponent> cross,
        CrossActionState state,
        uint actionId,
        EntityUid user,
        EntityUid target,
        bool handled,
        bool cancelled,
        string interruptionLoc
    )
    {
        if (cross.Comp.ActionState != state || cross.Comp.ActionId != actionId || cross.Comp.ActiveUser != user)
            return false;

        ClearAction(cross);

        if (cancelled)
        {
            if (Exists(user))
                _popup.PopupEntity(Loc.GetString(interruptionLoc), cross.Owner, user);
            return false;
        }

        return !handled && Exists(user) && Exists(target) && !TerminatingOrDeleted(target);
    }

    private bool TryStartCrossDoAfter(Entity<CrossComponent> cross, EntityUid user, TimeSpan delay, DoAfterEvent ev)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, delay, ev, cross.Owner, cross.Owner)
        {
            BreakOnMove = true,
            BreakOnWeightlessMove = false,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = SharedInteractionSystem.InteractionRange
        };
        return _doAfter.TryStartDoAfter(doAfter);
    }

    #endregion

    #region Action Queue & State

    private bool TryBeginAction(
        Entity<CrossComponent> cross,
        CrossActionState state,
        EntityUid user,
        EntityUid target,
        TimeSpan delay,
        bool popup
    )
    {
        if (cross.Comp.ActionState != CrossActionState.Idle &&
            cross.Comp.ActionDeadline is { } deadline &&
            _timing.CurTime <= deadline)
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("n14-cross-popup-busy"), cross.Owner, user);
            return false;
        }

        cross.Comp.NextActionId();
        cross.Comp.ActionState = state;
        cross.Comp.ActiveUser = user;
        cross.Comp.ActiveTarget = target;
        cross.Comp.ActionDeadline = _timing.CurTime + delay + TimeSpan.FromSeconds(1);
        return true;
    }

    private void ClearAction(Entity<CrossComponent> cross)
    {
        cross.Comp.ActionState = CrossActionState.Idle;
        cross.Comp.ActionDeadline = null;
        cross.Comp.ActiveUser = null;
        cross.Comp.ActiveTarget = null;
    }

    #endregion

    #region Hang / Unhang Core Logic

    private bool TryHangTarget(Entity<CrossComponent> cross, EntityUid target, EntityUid? user)
    {
        if (cross.Comp.HungTarget is { } current && current != target)
            return false;

        var hung = EnsureComp<HungOnCrossComponent>(target);
        hung.Cross = cross.Owner;
        Dirty(target, hung);
        cross.Comp.HungTarget = target;

        if (TryComp<PullableComponent>(target, out var pullable))
            _pulling.TryStopPull(target, pullable, user);
        if (TryComp<PullerComponent>(target, out var puller) && puller.Pulling is { } pulled)
            _pulling.TryStopPull(pulled);

        PositionHungTarget(cross, target);
        ApplyRestraints(cross, target);
        RefreshMobStateVisual(target);
        UpdateOccupiedOverlay(cross);
        return true;
    }

    private bool TryUnhangTarget(Entity<CrossComponent> cross, EntityUid target, bool applyBreakEffects)
    {
        if (!TryComp<HungOnCrossComponent>(target, out var hung) || hung.Cross != cross.Owner)
            return false;

        RemComp<HungOnCrossComponent>(target);
        if (cross.Comp.HungTarget == target)
            cross.Comp.HungTarget = null;

        if (!TerminatingOrDeleted(target) && !TerminatingOrDeleted(cross.Owner))
        {
            _transform.AttachToGridOrMap(target);
            var direction = _transform.GetWorldRotation(cross.Owner).GetCardinalDir();
            var offset = direction switch
            {
                Direction.North => new(0f, -cross.Comp.UnstrapDistance),
                Direction.South => new(0f, cross.Comp.UnstrapDistance),
                Direction.East => new(-cross.Comp.UnstrapDistance, 0f),
                Direction.West => new(cross.Comp.UnstrapDistance, 0f),
                _ => new Vector2(0f, -cross.Comp.UnstrapDistance)
            };
            var crossCoords = _transform.GetMapCoordinates(cross.Owner);
            _transform.SetMapCoordinates(target, new(crossCoords.Position + offset, crossCoords.MapId));
        }

        if (applyBreakEffects && !TerminatingOrDeleted(target))
        {
            _damageable.TryChangeDamage(target, cross.Comp.BreakDamage, origin: cross.Owner);
            _stun.TryParalyze(target, cross.Comp.BreakStunDuration, true);
        }

        RemoveRestraints(target);
        EnsureDownedAfterUnhang(target);
        RefreshMobStateVisual(target);
        UpdateOccupiedOverlay(cross);
        return true;
    }

    private void PositionHungTarget(Entity<CrossComponent> cross, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || TerminatingOrDeleted(cross.Owner))
            return;

        var offset = _crossSystem.GetHangOffset(cross.Owner, cross.Comp);
        _transform.SetCoordinates(target, Transform(target), new(cross.Owner, offset), Angle.Zero);
    }

    #endregion

    #region Lifecycle & Overlays

    private void OnCrossMapInit(Entity<CrossComponent> cross, ref MapInitEvent args)
    {
        cross.Comp.BreakInProgress = false;
        ClearAction(cross);

        if (cross.Comp.HungTarget is { } target &&
            TryComp<HungOnCrossComponent>(target, out var hung) &&
            hung.Cross == cross.Owner)
        {
            PositionHungTarget(cross, target);
            RefreshMobStateVisual(target);
        }

        UpdateOccupiedOverlay(cross);
    }

    private void OnCrossMove(Entity<CrossComponent> cross, ref MoveEvent args)
    {
        if (cross.Comp.HungTarget is { } target &&
            TryComp<HungOnCrossComponent>(target, out var hung) &&
            hung.Cross == cross.Owner)
            PositionHungTarget(cross, target);
    }

    private void OnCrossBroken(Entity<CrossComponent> cross, ref BreakageEventArgs args) =>
        HandleCrossDestruction(cross);

    private void OnCrossDestroyed(Entity<CrossComponent> cross, ref DestructionEventArgs args) =>
        HandleCrossDestruction(cross);

    private void OnCrossShutdown(Entity<CrossComponent> cross, ref ComponentShutdown args) =>
        HandleCrossDestruction(cross, true);

    private void HandleCrossDestruction(Entity<CrossComponent> cross, bool fromShutdown = false)
    {
        if (!fromShutdown)
            cross.Comp.BreakInProgress = true;

        ClearAction(cross);

        if (cross.Comp.HungTarget is { } target &&
            TryComp<HungOnCrossComponent>(target, out var hung) &&
            hung.Cross == cross.Owner)
            TryUnhangTarget(cross, target, cross.Comp.BreakInProgress);

        if (cross.Comp.OccupiedOverlayEntity is { } overlay && !TerminatingOrDeleted(overlay))
            QueueDel(overlay);

        cross.Comp.OccupiedOverlayEntity = null;
        cross.Comp.HungTarget = null;
    }

    private void UpdateOccupiedOverlay(Entity<CrossComponent> cross)
    {
        var hasTarget = cross.Comp.HungTarget != null;

        if (hasTarget && cross.Comp.OccupiedOverlayEntity == null)
        {
            cross.Comp.OccupiedOverlayEntity = Spawn(
                cross.Comp.OccupiedOverlayPrototype,
                new(cross.Owner, Vector2.Zero));
        }
        else if (!hasTarget && cross.Comp.OccupiedOverlayEntity is { } overlay)
        {
            if (!TerminatingOrDeleted(overlay))
                QueueDel(overlay);
            cross.Comp.OccupiedOverlayEntity = null;
        }
    }

    #endregion

    #region Restraints & Visuals

    private void OnUncuffAttempt(ref UncuffAttemptEvent args)
    {
        if (args.Cancelled || !TryComp<HungOnCrossComponent>(args.Target, out var hung) ||
            hung.Cross is not { } crossUid)
            return;

        if (HasCrossRestraints(args.Target))
        {
            args.Cancelled = true;

            if (Exists(args.User))
            {
                if (TryComp<CrossComponent>(crossUid, out var crossComp))
                    _ = TryStartUnhangAction((crossUid, crossComp), args.User, args.Target, true);
                else
                    _popup.PopupEntity(Loc.GetString("n14-cross-popup-cant-uncuff-while-hung"), args.User, args.User);
            }
        }
    }

    private void ApplyRestraints(Entity<CrossComponent> cross, EntityUid target)
    {
        if (!TryComp<HandsComponent>(target, out _))
            return;

        var cuffable = EnsureComp<CuffableComponent>(target);
        if (HasCrossRestraints((target, cuffable)))
            return;

        var restraints = Spawn(cross.Comp.RestraintPrototype, Transform(target).Coordinates);
        if (!TryComp<HandcuffComponent>(restraints, out _) ||
            !_cuffs.TryAddNewCuffs(target, target, restraints, cuffable))
            QueueDel(restraints);
    }

    private void RemoveRestraints(EntityUid target)
    {
        if (!TryComp<CuffableComponent>(target, out var cuffable))
            return;

        var toRemove = new List<EntityUid>();
        foreach (var cuffs in _cuffs.GetAllCuffs(cuffable))
            if (HasComp<CrossRestraintComponent>(cuffs))
                toRemove.Add(cuffs);

        foreach (var cuffs in toRemove)
        {
            var cuffsBefore = cuffable.CuffedHandCount;
            _cuffs.Uncuff(target, null, cuffs, cuffable);

            if (cuffable.CuffedHandCount == cuffsBefore)
                _container.Remove(cuffs, cuffable.Container, force: true);

            if (!TerminatingOrDeleted(cuffs))
                QueueDel(cuffs);
        }
    }

    private bool HasCrossRestraints(EntityUid target) =>
        TryComp<CuffableComponent>(target, out var cuffable) && HasCrossRestraints((target, cuffable));

    private bool HasCrossRestraints(Entity<CuffableComponent> ent)
    {
        foreach (var cuffs in _cuffs.GetAllCuffs(ent.Comp))
            if (HasComp<CrossRestraintComponent>(cuffs))
                return true;

        return false;
    }

    private void EnsureDownedAfterUnhang(EntityUid target)
    {
        if (TryComp<MobStateComponent>(target, out var mobState) &&
            mobState.CurrentState is MobState.Dead or MobState.Critical or MobState.SoftCritical)
            _standing.Down(target, false, false);
    }

    private void OnMobStateChanged(EntityUid uid, HungOnCrossComponent component, MobStateChangedEvent args) =>
        RefreshMobStateVisual(uid);

    private void OnDowned(EntityUid uid, HungOnCrossComponent component, ref DownedEvent args) =>
        _appearance.SetData(uid, RotationVisuals.RotationState, RotationState.Vertical);

    private void RefreshMobStateVisual(EntityUid target)
    {
        if (!TryComp<MobStateComponent>(target, out var mobState))
            return;

        var isHung = TryComp<HungOnCrossComponent>(target, out var hung) && hung.Cross != null;
        var visualState = isHung && mobState.CurrentState is MobState.Critical or MobState.SoftCritical
            ? MobState.Alive
            : mobState.CurrentState;

        _appearance.SetData(target, MobStateVisuals.State, visualState);

        if (isHung)
            _appearance.SetData(target, RotationVisuals.RotationState, RotationState.Vertical);
        else if (TryComp<StandingStateComponent>(target, out var standing))
        {
            _appearance.SetData(
                target,
                RotationVisuals.RotationState,
                standing.CurrentState == StandingState.Standing ? RotationState.Vertical : RotationState.Horizontal);
        }
    }

    #endregion
}
