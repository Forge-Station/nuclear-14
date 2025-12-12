using System.Numerics;
using Content.Shared._NC.Mountable.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.Standing;

namespace Content.Shared._NC.Mountable;

/// <summary>
/// The system responsible for mount management
/// </summary>
public sealed class SharedMountSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RiderComponent, MoveEvent>(OnRiderMove);
        SubscribeLocalEvent<RiderComponent, MobStateChangedEvent>(OnMobStateChanged);

        SubscribeLocalEvent<MountableComponent, DownAttemptEvent>(OnDownAttempt);
        SubscribeLocalEvent<MountableComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<MountableComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<MountableComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>
    /// Responsible for shifting the rider when riding
    /// </summary>
    private void OnRiderMove(Entity<RiderComponent> ent, ref MoveEvent args)
    {
        if (ent.Comp.Mount is not { } mount || !TryComp<MountableComponent>(mount, out var mountable)
            || !TryComp(mount, out TransformComponent? mountXform))
            return;

        var direction = mountXform.LocalRotation.GetDir();
        var offset = mountable.RiderOffset + mountable.DirectionOffsets.GetValueOrDefault(direction, Vector2.Zero);
        _transform.SetLocalPositionNoLerp(ent.Owner, mountXform.LocalPosition + offset);
    }

    /// <summary>
    /// Checking that you can't drop a mount with a rider
    /// </summary>
    private void OnDownAttempt(Entity<MountableComponent> ent, ref DownAttemptEvent args)
    {
        if (ent.Comp.Rider != null)
            args.Cancel();
    }

    /// <summary>
    /// The method of attaching a rider to a mount
    /// </summary>
    private void OnStrapped(Entity<MountableComponent> ent, ref StrappedEvent args)
    {
        if (_mobState.IsDead(ent))
        {
            _buckle.TryUnbuckle(args.Buckle.Owner, ent.Owner);
            return;
        }

        EnsureComp<InputMoverComponent>(ent);
        var rider = EnsureComp<RiderComponent>(args.Buckle.Owner);
        rider.Mount = ent.Owner;
        ent.Comp.Rider = args.Buckle.Owner;

        Dirty(ent.Owner, ent.Comp);
        Dirty(args.Buckle.Owner, rider);

        if (ent.Comp.ControlMovement)
        {
            _mover.SetRelay(args.Buckle.Owner, ent.Owner);
        }

        if (_standing.IsDown(ent))
            _standing.Stand(ent);

        RemComp<ActiveNPCComponent>(ent);
        if (!TryComp<MovementSpeedModifierComponent>(ent, out var move))
            return;

        var walk = move.BaseWalkSpeed * ent.Comp.MountedSpeed;
        var sprint = move.BaseSprintSpeed * ent.Comp.MountedSpeed;
        _movement.ChangeBaseSpeed(ent, walk, sprint, move.Acceleration, move);
    }

    /// <summary>
    /// Unties and removes the rider
    /// </summary>
    private void OnUnstrapped(Entity<MountableComponent> ent, ref UnstrappedEvent args)
    {
        if (HasComp<RelayInputMoverComponent>(args.Buckle.Owner))
            RemComp<RelayInputMoverComponent>(args.Buckle.Owner);

        RemComp<RiderComponent>(args.Buckle.Owner);
        ent.Comp.Rider = null;

        Dirty(ent.Owner, ent.Comp);

        AddComp(ent, new ActiveNPCComponent());
        if (!TryComp<MovementSpeedModifierComponent>(ent, out var move))
            return;

        var walk = move.BaseWalkSpeed / ent.Comp.DefaultSpeed;
        var sprint = move.BaseSprintSpeed / ent.Comp.DefaultSpeed;
        _movement.ChangeBaseSpeed(ent, walk, sprint, move.Acceleration, move);
    }

    /// <summary>
    /// Resets the rider if he starts to die
    /// </summary>
    private void OnMobStateChanged(Entity<RiderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive || ent.Comp.Mount == null)
            return;

        _buckle.TryUnbuckle(ent.Owner, ent.Comp.Mount);
    }

    /// <summary>
    /// Resets the rider if the mount starts to die and returns everything as it was if everything is fine with it
    /// </summary>
    private void OnMobStateChanged(Entity<MountableComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
        {
            ent.Comp.ControlMovement = false;
            if (ent.Comp.Rider != null)
                _buckle.TryUnbuckle(ent.Comp.Rider.Value, ent.Owner);
        }
        else
        {
            ent.Comp.ControlMovement = true;
            if (ent.Comp.Rider != null)
                _buckle.TryUnbuckle(ent.Comp.Rider.Value, ent.Owner);
        }
    }
}
