using System.Numerics;
using Content.Shared.DragDrop;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Pulling.Events;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;


namespace Content.Shared._Forge.Cross;


public sealed class CrossSystem : EntitySystem
{
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

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

        if (args.Handled || dragged == cross.Owner || TerminatingOrDeleted(dragged))
            return;

        if (!TryComp<HandsComponent>(dragged, out var hands) || hands.Count == 0)
            return;

        args.CanDrop = _interaction.InRangeUnobstructed(
            dragged,
            cross.Owner,
            predicate: e => e == user || e == dragged || e == cross.Owner,
            popup: false);

        args.Handled = true;
    }

    private void OnBeingPulledAttempt(Entity<HungOnCrossComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (ent.Comp.Cross is { } cross && HasComp<CrossComponent>(cross))
            args.Cancel();
    }

    private void OnHungChangeDirectionAttempt(Entity<HungOnCrossComponent> ent, ref ChangeDirectionAttemptEvent args) =>
        args.Cancel();

    private void OnCrossPreventCollide(Entity<CrossComponent> cross, ref PreventCollideEvent args)
    {
        if (args.Cancelled || !TryComp<HungOnCrossComponent>(args.OtherEntity, out var hung))
            return;

        if (hung.Cross == cross.Owner)
            args.Cancelled = true;
    }

    private void OnHungMove(Entity<HungOnCrossComponent> ent, ref MoveEvent args)
    {
        if (_timing.ApplyingState || TerminatingOrDeleted(ent.Owner) || ent.Comp.Cross is not { } cross ||
            TerminatingOrDeleted(cross))
            return;

        if (!TryComp<CrossComponent>(cross, out var crossComp))
            return;

        var xform = args.Component;
        var offset = GetHangOffset(cross, crossComp);

        if (xform.ParentUid == cross && (xform.LocalPosition - offset).LengthSquared() <= 1e-5f)
            return;

        _transform.SetCoordinates(ent.Owner, xform, new(cross, offset), Angle.Zero);
    }

    public Vector2 GetHangOffset(EntityUid cross, CrossComponent comp)
    {
        var direction = _transform.GetWorldRotation(cross).GetCardinalDir();
        return comp.GetBuckleOffset(direction, Vector2.Zero);
    }
}
