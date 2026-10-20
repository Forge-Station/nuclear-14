// Forge-Change: reuse the steering buffer, reject stale paths, and profile NPC steering.
using System.Numerics;
using System.Threading;
using Content.Server.Administration.Managers;
using Content.Server.DoAfter;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Server.NPC.Pathfinding;
using Content.Shared.CCVar;
using Content.Shared.Climbing.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.NPC.Events;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Shared.Prying.Systems;
using Microsoft.Extensions.ObjectPool;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem : SharedNPCSteeringSystem
{
    /*
     * We use context steering to determine which way to move.
     * This involves creating an array of possible directions and assigning a value for the desireability of each direction.
     *
     * There's multiple ways to implement this, e.g. you can average all directions, or you can choose the highest direction
     * , or you can remove the danger map entirely and only having an interest map (AKA game endeavour).
     * See http://www.gameaipro.com/GameAIPro2/GameAIPro2_Chapter18_Context_Steering_Behavior-Driven_Steering_at_the_Macro_Scale.pdf
     * (though in their case it was for an F1 game so used context steering across the width of the road).
     */

    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private readonly PryingSystem _pryingSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<MovementSpeedModifierComponent> _modifierQuery;
    private EntityQuery<NpcFactionMemberComponent> _factionQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private ObjectPool<HashSet<EntityUid>> _entSetPool =
        new DefaultObjectPool<HashSet<EntityUid>>(new SetPolicy<EntityUid>());

    // Forge-Change-Start: reuse the NPC buffer across ticks.
    private (EntityUid, NPCSteeringComponent, InputMoverComponent, TransformComponent)[] _npcPool =
        Array.Empty<(EntityUid, NPCSteeringComponent, InputMoverComponent, TransformComponent)>();
    // Forge-Change-End

    /// <summary>
    /// Enabled antistuck detection so if an NPC is in the same spot for a while it will re-path.
    /// </summary>
    public bool AntiStuck = true;

    private bool _enabled;

    private bool _pathfinding = true;

    public static readonly Vector2[] Directions = new Vector2[InterestDirections];

    private readonly HashSet<ICommonSession> _subscribedSessions = new();

    private object _obstacles = new();

    public override void Initialize()
    {
        base.Initialize();

        Log.Level = LogLevel.Info;
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _modifierQuery = GetEntityQuery<MovementSpeedModifierComponent>();
        _factionQuery = GetEntityQuery<NpcFactionMemberComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        for (var i = 0; i < InterestDirections; i++)
        {
            Directions[i] = new Angle(InterestRadians * i).ToVec();
        }

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        Subs.CVar(_configManager, CCVars.NPCEnabled, SetNPCEnabled, true);
        Subs.CVar(_configManager, CCVars.NPCPathfinding, SetNPCPathfinding, true);
        Subs.CVar(_configManager, CCVars.NPCPathfindingProfile, SetProfileEnabled, true); // Forge-Change

        SubscribeLocalEvent<NPCSteeringComponent, ComponentShutdown>(OnSteeringShutdown);
        SubscribeNetworkEvent<RequestNPCSteeringDebugEvent>(OnDebugRequest);
    }

    private void SetNPCEnabled(bool obj)
    {
        if (!obj)
        {
            foreach (var (comp, mover) in EntityQuery<NPCSteeringComponent, InputMoverComponent>())
            {
                mover.CurTickSprintMovement = Vector2.Zero;
                comp.PathfindToken?.Cancel();
                comp.PathfindToken = null;
            }
        }

        _enabled = obj;
    }

    private void SetNPCPathfinding(bool value)
    {
        _pathfinding = value;

        if (!_pathfinding)
        {
            foreach (var comp in EntityQuery<NPCSteeringComponent>(true))
            {
                comp.PathfindToken?.Cancel();
                comp.PathfindToken = null;
            }
        }
    }

    private void OnDebugRequest(RequestNPCSteeringDebugEvent msg, EntitySessionEventArgs args)
    {
        if (!_admin.IsAdmin(args.SenderSession))
            return;

        if (msg.Enabled)
            _subscribedSessions.Add(args.SenderSession);
        else
            _subscribedSessions.Remove(args.SenderSession);
    }

    private void OnSteeringShutdown(EntityUid uid, NPCSteeringComponent component, ComponentShutdown args)
    {
        // Cancel any active pathfinding jobs as they're irrelevant.
        component.PathfindToken?.Cancel();
        component.PathfindToken = null;
    }

    /// <summary>
    /// Adds the AI to the steering system to move towards a specific target
    /// </summary>
    public NPCSteeringComponent Register(EntityUid uid, EntityCoordinates coordinates, NPCSteeringComponent? component = null)
    {
        if (Resolve(uid, ref component, false))
        {
            if (component.Coordinates.Equals(coordinates))
                return component;

            component.PathfindToken?.Cancel();
            component.PathfindToken = null;
            component.CurrentPath.Clear();
        }
        else
        {
            component = AddComp<NPCSteeringComponent>(uid);
            component.Flags = _pathfindingSystem.GetFlags(uid);
        }

        ResetStuck(component, Transform(uid).Coordinates);
        component.Coordinates = coordinates;
        return component;
    }

    /// <summary>
    /// Attempts to register the entity. Does nothing if the coordinates already registered.
    /// </summary>
    public bool TryRegister(EntityUid uid, EntityCoordinates coordinates, NPCSteeringComponent? component = null)
    {
        if (Resolve(uid, ref component, false) && component.Coordinates.Equals(coordinates))
        {
            return false;
        }

        Register(uid, coordinates, component);
        return true;
    }

    /// <summary>
    /// Stops the steering behavior for the AI and cleans up.
    /// </summary>
    public void Unregister(EntityUid uid, NPCSteeringComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (EntityManager.TryGetComponent(uid, out InputMoverComponent? controller))
        {
            controller.CurTickSprintMovement = Vector2.Zero;

            var ev = new SpriteMoveEvent(false);
            RaiseLocalEvent(uid, ref ev);
        }

        component.PathfindToken?.Cancel();
        component.PathfindToken = null;
        RemComp<NPCSteeringComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        // Not every mob has the modifier component so do it as a separate query.
        // Forge-Change-Start: fill the reusable buffer without per-tick allocation.
        var count = Count<ActiveNPCComponent>();
        if (_npcPool.Length < count)
            _npcPool = new (EntityUid, NPCSteeringComponent, InputMoverComponent, TransformComponent)[count];
        // Forge-Change-End

        var query = EntityQueryEnumerator<ActiveNPCComponent, NPCSteeringComponent, InputMoverComponent, TransformComponent>();
        var index = 0;

        while (query.MoveNext(out var uid, out _, out var steering, out var mover, out var xform))
        {
            _npcPool[index] = (uid, steering, mover, xform); // Forge-Change
            index++;
        }

        // Forge-Change-Start: run steering directly and measure it when requested.
        var curTime = _timing.CurTime;
        var profileStart = _profileEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var following = 0;

        for (var i = 0; i < index; i++)
        {
            var (uid, steering, mover, xform) = _npcPool[i];
            if (_profileEnabled && steering.CurrentPath.Count > 0)
                following++;
            Steer(uid, steering, mover, xform, frameTime, curTime);
        }

        if (_profileEnabled)
            RecordSteeringProfile(System.Diagnostics.Stopwatch.GetElapsedTime(profileStart).TotalMilliseconds, index, following);
        // Forge-Change-End


        if (_subscribedSessions.Count > 0)
        {
            var data = new List<NPCSteeringDebugData>(index);

            for (var i = 0; i < index; i++)
            {
                var (uid, steering, mover, _) = _npcPool[i]; // Forge-Change

                data.Add(new NPCSteeringDebugData(
                    GetNetEntity(uid),
                    mover.CurTickSprintMovement,
                    steering.Interest,
                    steering.Danger,
                    steering.DangerPoints));
            }

            var filter = Filter.Empty();
            filter.AddPlayers(_subscribedSessions);

            RaiseNetworkEvent(new NPCSteeringDebugEvent(data), filter);
        }
    }

    private void SetDirection(EntityUid uid, InputMoverComponent component, NPCSteeringComponent steering, Vector2 value, bool clear = true)
    {
        if (clear && value.Equals(Vector2.Zero))
        {
            steering.CurrentPath.Clear();
            Array.Clear(steering.Interest);
            Array.Clear(steering.Danger);
        }

        component.CurTickSprintMovement = value;
        component.LastInputTick = _timing.CurTick;
        component.LastInputSubTick = ushort.MaxValue;

        var ev = new SpriteMoveEvent(true);
        RaiseLocalEvent(uid, ref ev);
    }

    /// <summary>
    /// Go through each steerer and combine their vectors
    /// </summary>
    private void Steer(
        EntityUid uid,
        NPCSteeringComponent steering,
        InputMoverComponent mover,
        TransformComponent xform,
        float frameTime,
        TimeSpan curTime)
    {
        if (Deleted(steering.Coordinates.EntityId))
        {
            SetDirection(uid, mover, steering, Vector2.Zero);
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        // No path set from pathfinding or the likes.
        if (steering.Status == SteeringStatus.NoPath)
        {
            // Forge-Change: MoveTo may have finished while melee combat keeps steering active.
            // Clear distant retaliation even when NoPath was set outside the path request callback.
            ForgetUnreachableAttacker(uid, steering);
            SetDirection(uid, mover, steering, Vector2.Zero);
            return;
        }

        // Can't move at all, just noop input.
        if (!mover.CanMove)
        {
            SetDirection(uid, mover, steering, Vector2.Zero);
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        var agentRadius = steering.Radius;
        var worldPos = _transform.GetWorldPosition(xform);
        var (layer, mask) = _physics.GetHardCollision(uid);

        // Use rotation relative to parent to rotate our context vectors by.
        var offsetRot = -_mover.GetParentGridAngle(mover);
        _modifierQuery.TryGetComponent(uid, out var modifier);
        var moveSpeed = GetSprintSpeed(uid, modifier);
        var body = _physicsQuery.GetComponent(uid);
        var dangerPoints = steering.DangerPoints;
        dangerPoints.Clear();
        Span<float> interest = stackalloc float[InterestDirections];
        Span<float> danger = stackalloc float[InterestDirections];

        // TODO: This should be fly
        steering.CanSeek = true;

        var ev = new NPCSteeringEvent(steering, xform, worldPos, offsetRot);
        RaiseLocalEvent(uid, ref ev);
        // If seek has arrived at the target node for example then immediately re-steer.
        var forceSteer = true;

        if (steering.CanSeek && !TrySeek(uid, mover, steering, body, xform, offsetRot, moveSpeed, interest, frameTime, ref forceSteer))
        {
            // Forge-Change: TrySeek can also mark the route unreachable after MoveTo has finished.
            if (steering.Status == SteeringStatus.NoPath)
                ForgetUnreachableAttacker(uid, steering);
            SetDirection(uid, mover, steering, Vector2.Zero);
            return;
        }

        DebugTools.Assert(!float.IsNaN(interest[0]));

        // Don't steer too frequently to avoid twitchiness.
        // This should also implicitly solve tie situations.
        // I think doing this after all the ops above is best?
        // Originally I had it way above but sometimes mobs would overshoot their tile targets.

        if (!forceSteer)
        {
            SetDirection(uid, mover, steering, steering.LastSteerDirection, false);
            return;
        }

        // Avoid static objects like walls
        CollisionAvoidance(uid, offsetRot, worldPos, agentRadius, layer, mask, xform, danger);
        DebugTools.Assert(!float.IsNaN(danger[0]));

        Separation(uid, offsetRot, worldPos, agentRadius, layer, mask, body, xform, danger);

        // Blend last and current tick
        Blend(steering, frameTime, interest, danger);

        // Remove the danger map from the interest map.
        var desiredDirection = -1;
        var desiredValue = 0f;

        for (var i = 0; i < InterestDirections; i++)
        {
            var adjustedValue = Math.Clamp(steering.Interest[i] - steering.Danger[i], 0f, 1f);

            if (adjustedValue > desiredValue)
            {
                desiredDirection = i;
                desiredValue = adjustedValue;
            }
        }

        var resultDirection = Vector2.Zero;

        if (desiredDirection != -1)
        {
            resultDirection = new Angle(desiredDirection * InterestRadians).ToVec();
        }

        steering.LastSteerDirection = resultDirection;
        DebugTools.Assert(!float.IsNaN(resultDirection.X));
        SetDirection(uid, mover, steering, resultDirection, false);
    }

    private EntityCoordinates GetCoordinates(PathPoly poly)
    {
        if (!poly.IsValid())
            return EntityCoordinates.Invalid;

        return new EntityCoordinates(poly.GraphUid, poly.Box.Center);
    }

    /// <summary>
    /// Get a new job from the pathfindingsystem
    /// </summary>
    private async void RequestPath(EntityUid uid, NPCSteeringComponent steering, TransformComponent xform, float targetDistance)
    {
        // If we already have a pathfinding request then don't grab another.
        // If we're in range then just beeline them; this can avoid stutter stepping and is an easy way to look nicer.
        if (steering.Pathfind || targetDistance < steering.RepathRange)
            return;

        // Short-circuit with no path.
        var targetPoly = _pathfindingSystem.GetPoly(steering.Coordinates);

        // If this still causes issues future sloth adjust the collision mask.
        // Thanks past sloth I already realised.
        if (targetPoly != null &&
            steering.Coordinates.Position.Equals(Vector2.Zero) &&
            TryComp<PhysicsComponent>(uid, out var physics) &&
            _interaction.InRangeUnobstructed(uid, steering.Coordinates.EntityId, range: 30f, (CollisionGroup)physics.CollisionMask))
        {
            steering.CurrentPath.Clear();
            steering.CurrentPath.Enqueue(targetPoly);
            return;
        }

        // Forge-Change-Start: keep the token and target tied to this path request.
        var pathfindToken = new CancellationTokenSource();
        if (_profileEnabled)
            _profileRequests++;
        var pathfindTarget = steering.Coordinates;
        steering.PathfindToken = pathfindToken;
        // Forge-Change-End

        var flags = _pathfindingSystem.GetFlags(uid);

        var result = await _pathfindingSystem.GetPathSafe(
            uid,
            xform.Coordinates,
            pathfindTarget, // Forge-Change
            steering.Range,
            pathfindToken.Token, // Forge-Change
            flags);

        // Forge-Change-Start: ignore path results from superseded requests.
        // A previous request may finish after the target or steering component changes.
        // Its result must not replace a newer route or clear a newer request token.
        if (!Exists(uid) ||
            !TryComp<NPCSteeringComponent>(uid, out var currentSteering) ||
            !ReferenceEquals(currentSteering, steering) ||
            steering.PathfindToken != pathfindToken ||
            pathfindToken.IsCancellationRequested ||
            !steering.Coordinates.Equals(pathfindTarget))
        {
            return;
        }
        // Forge-Change-End

        steering.PathfindToken = null;

        if (result.Result == PathResult.NoPath)
        {
            steering.CurrentPath.Clear();
            steering.FailedPathCount++;

            // Forge-Change: stop moving into obstacles once the remembered player has no route.
            var forgotAttacker = ForgetUnreachableAttacker(uid, steering);
            if (forgotAttacker || steering.FailedPathCount >= NPCSteeringComponent.FailedPathLimit)
            {
                steering.Status = SteeringStatus.NoPath;
                if (TryComp<InputMoverComponent>(uid, out var mover))
                    SetDirection(uid, mover, steering, Vector2.Zero);
            }

            return;
        }

        // Forge-Change: re-pathing must obey the same detour limit as HTN planning.
        if (_pathfindingSystem.ExceedsPlayerChaseDetour(uid, xform.Coordinates, pathfindTarget, result.Path))
        {
            steering.Status = SteeringStatus.NoPath;
            steering.CurrentPath.Clear();
            ForgetUnreachableAttacker(uid, steering);
            if (TryComp<InputMoverComponent>(uid, out var mover))
                SetDirection(uid, mover, steering, Vector2.Zero);
            return;
        }

        var targetPos = steering.Coordinates.ToMap(EntityManager, _transform);
        var ourPos = _transform.GetMapCoordinates(uid, xform: xform);

        PrunePath(uid, ourPos, targetPos.Position - ourPos.Position, result.Path);
        steering.CurrentPath = new Queue<PathPoly>(result.Path);
        steering.FailedPathCount = 0; // Forge-Change
    }

    // TODO: Move these to movercontroller

    private float GetSprintSpeed(EntityUid uid, MovementSpeedModifierComponent? modifier = null)
    {
        if (!Resolve(uid, ref modifier, false))
        {
            return MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
        }

        return modifier.CurrentSprintSpeed;
    }
}
