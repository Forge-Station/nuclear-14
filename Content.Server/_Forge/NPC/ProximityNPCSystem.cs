// Forge-Change: sleep distant map NPCs and wake them when players approach or attack.
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared._Forge.NPC;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared._NC.Mountable.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.NPC;

/// <summary>
/// Checks map NPCs in small batches and sleeps those far from every player.
/// </summary>
public sealed class ProximityNPCSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly List<EntityUid> _pending = new();
    private readonly HashSet<EntityUid> _proximitySleeping = new();
    private readonly Dictionary<EntityUid, TimeSpan> _retaliatingUntil = new();
    private readonly HashSet<Entity<ActorComponent>> _players = new();
    private EntityQuery<TransformComponent> _xformQuery;
    private int _pendingIndex;
    private int _budgetPerTick;
    private float _elapsed;
    private float _checkInterval;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        Subs.CVar(_config, CCVars.NPCProximityEnabled, SetEnabled, true);
        Subs.CVar(_config, CCVars.NPCProximityCheckInterval,
            value => _checkInterval = Math.Max(0.5f, value), true);

        SubscribeLocalEvent<ProximityNPCComponent, MapInitEvent>(OnMapInit,
            after: [typeof(HTNSystem)]);
        SubscribeLocalEvent<ProximityNPCComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ProximityNPCComponent, ComponentShutdown>(OnShutdown);
    }

    private void SetEnabled(bool enabled)
    {
        if (_enabled && !enabled)
        {
            foreach (var uid in _proximitySleeping)
            {
                if (!Exists(uid) || HasComp<ActorComponent>(uid) || !HasComp<HTNComponent>(uid))
                    continue;

                EnsureComp<InputMoverComponent>(uid);
                if (TryComp<MobStateComponent>(uid, out var state) && state.CurrentState == MobState.Alive)
                    _npc.WakeNPC(uid);
            }

            _proximitySleeping.Clear();
            _retaliatingUntil.Clear();
            _pending.Clear();
            _pendingIndex = 0;
        }

        _enabled = enabled;
    }

    private void OnMapInit(Entity<ProximityNPCComponent> ent, ref MapInitEvent args)
    {
        if (!_enabled || !ent.Comp.StartAsleep || HasComp<ActorComponent>(ent) || !HasComp<HTNComponent>(ent))
            return;

        Sleep(ent);
    }

    private void OnPlayerAttached(Entity<ProximityNPCComponent> ent, ref PlayerAttachedEvent args)
    {
        _proximitySleeping.Remove(ent);
        _retaliatingUntil.Remove(ent);
        EnsureComp<InputMoverComponent>(ent);
    }

    private void OnShutdown(Entity<ProximityNPCComponent> ent, ref ComponentShutdown args)
    {
        _proximitySleeping.Remove(ent);
        _retaliatingUntil.Remove(ent);
    }

    /// <summary>
    /// A distant NPC that was hit must stay awake long enough to pursue its attacker.
    /// </summary>
    public void WakeForRetaliation(EntityUid uid, TimeSpan until)
    {
        if (!_enabled || !HasComp<ProximityNPCComponent>(uid) || HasComp<ActorComponent>(uid) ||
            !HasComp<HTNComponent>(uid) ||
            !TryComp<MobStateComponent>(uid, out var state) || state.CurrentState != MobState.Alive)
            return;

        _retaliatingUntil[uid] = until;
        _proximitySleeping.Remove(uid);
        EnsureComp<InputMoverComponent>(uid);
        _npc.WakeNPC(uid);
    }

    /// <summary>
    /// Let normal proximity checks put an unreachable NPC back to sleep.
    /// </summary>
    public void CancelRetaliation(EntityUid uid)
    {
        _retaliatingUntil.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabled)
            return;

        _elapsed += frameTime;

        if (_pendingIndex < _pending.Count)
        {
            ProcessBatch();
            return;
        }

        if (_elapsed < _checkInterval)
            return;

        _elapsed = 0f;
        _pending.Clear();
        var query = EntityQueryEnumerator<ProximityNPCComponent>();
        while (query.MoveNext(out var uid, out _))
            _pending.Add(uid);

        _pendingIndex = 0;
        if (_pending.Count == 0)
            return;

        var ticksAvailable = _checkInterval * _timing.TickRate;
        _budgetPerTick = Math.Max(1, (int) Math.Ceiling(_pending.Count / ticksAvailable));
        ProcessBatch();
    }

    private void ProcessBatch()
    {
        var end = Math.Min(_pendingIndex + _budgetPerTick, _pending.Count);
        for (var i = _pendingIndex; i < end; i++)
        {
            var uid = _pending[i];
            if (!TryComp<ProximityNPCComponent>(uid, out var proximity) ||
                !_xformQuery.TryGetComponent(uid, out var xform) ||
                xform.MapID == MapId.Nullspace ||
                HasComp<ActorComponent>(uid) || !HasComp<HTNComponent>(uid) ||
                !TryComp<MobStateComponent>(uid, out var state))
                continue;

            // Forge-Change: a ridden mount is controlled by its rider; do not wake its HTN.
            if (TryComp<MountableComponent>(uid, out var mount) && mount.Rider != null)
                continue;

            if (state.CurrentState != MobState.Alive)
            {
                _retaliatingUntil.Remove(uid);
                continue;
            }

            var awake = _npc.IsAwake(uid);
            // NPCSystem wakes revived mobs independently. Restore the mover we removed for sleep.
            if (awake && _proximitySleeping.Remove(uid))
                EnsureComp<InputMoverComponent>(uid);
            if (_retaliatingUntil.TryGetValue(uid, out var until))
            {
                if (_timing.CurTime < until)
                    continue;

                _retaliatingUntil.Remove(uid);
            }

            var range = awake ? proximity.SleepRange : proximity.WakeRange;
            var mapPos = _transform.GetMapCoordinates(uid, xform);
            _players.Clear();
            _lookup.GetEntitiesInRange(mapPos, range, _players);
            var playerNearby = _players.Count > 0;

            if (awake && !playerNearby)
                Sleep(uid);
            else if (!awake && playerNearby && _proximitySleeping.Remove(uid))
            {
                EnsureComp<InputMoverComponent>(uid);
                _npc.WakeNPC(uid);
            }
        }

        _pendingIndex = end;
    }

    private void Sleep(EntityUid uid)
    {
        _npc.SleepNPC(uid);
        RemCompDeferred<InputMoverComponent>(uid);
        _proximitySleeping.Add(uid);
    }
}
