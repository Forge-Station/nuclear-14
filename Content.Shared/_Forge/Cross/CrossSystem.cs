using System.Numerics;
using Content.Shared.DragDrop;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Pulling.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.Cross;

public sealed class CrossSystem : EntitySystem
{
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrossComponent, CanDropTargetEvent>(OnCanDropTarget);
        SubscribeLocalEvent<CrossComponent, PreventCollideEvent>(OnCrossPreventCollide);
        SubscribeLocalEvent<HungOnCrossComponent, BeingPulledAttemptEvent>(OnBeingPulledAttempt);
        SubscribeLocalEvent<HungOnCrossComponent, ChangeDirectionAttemptEvent>(OnHungChangeDirectionAttempt);
        SubscribeLocalEvent<HungOnCrossComponent, MoveEvent>(OnHungMove);
    }

    private void OnCanDropTarget(Entity<CrossComponent> cross, ref CanDropTargetEvent args)
    {
        var user = args.User;
        var dragged = args.Dragged;
        var crossUid = cross.Owner;

        if (args.Handled || dragged == crossUid || TerminatingOrDeleted(dragged) || !TryComp<HandsComponent>(dragged, out var hands) || hands.Count <= 0)
            return;

        bool Ignored(EntityUid ent) => ent == user || ent == dragged || ent == crossUid;
        args.CanDrop = _interaction.InRangeUnobstructed(
            dragged,
            crossUid,
            predicate: Ignored,
            popup: false);

        args.Handled = true;
    }

    private void OnBeingPulledAttempt(Entity<HungOnCrossComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (ent.Comp.Cross is not { } cross || !HasComp<CrossComponent>(cross))
            return;

        args.Cancel();
    }

    private void OnHungChangeDirectionAttempt(Entity<HungOnCrossComponent> ent, ref ChangeDirectionAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnCrossPreventCollide(Entity<CrossComponent> cross, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<HungOnCrossComponent>(args.OtherEntity, out var hung))
            return;

        if (hung.Cross != cross.Owner)
            return;

        args.Cancelled = true;
    }

    private void OnHungMove(Entity<HungOnCrossComponent> ent, ref MoveEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (ent.Comp.Cross is not { } cross || !TryComp<CrossComponent>(cross, out var crossComp))
            return;

        if (TerminatingOrDeleted(cross))
            return;

        var direction = _transform.GetWorldRotation(cross).GetCardinalDir();
        var offset = crossComp.GetBuckleOffset(direction, Vector2.Zero);

        var xform = args.Component;
        if (xform.ParentUid == cross && (xform.LocalPosition - offset).LengthSquared() <= 1e-5f)
            return;

        var coords = new EntityCoordinates(cross, offset);
        _transform.SetCoordinates(ent.Owner, xform, coords, rotation: Angle.Zero);
    }
}

