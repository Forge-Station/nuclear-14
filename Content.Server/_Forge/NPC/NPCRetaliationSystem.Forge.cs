// Forge-Change: proximity NPC retaliation, ally assistance, and memory cleanup.
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server._Forge.NPC;
using Content.Shared.CCVar;
using Content.Shared._Forge.NPC;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCRetaliationSystem
{
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly ProximityNPCSystem _proximity = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;

    private float _allyAssistRange;
    private float _playerRetaliationDuration;

    private readonly List<EntityUid> _expired = new();
    private readonly HashSet<Entity<NpcFactionMemberComponent>> _nearbyAllies = new();

    private void InitializeForgeRetaliation()
    {
        Subs.CVar(_config, CCVars.NPCRetaliationAssistRange,
            value => _allyAssistRange = Math.Max(0f, value), true);
        Subs.CVar(_config, CCVars.NPCPlayerRetaliationDuration,
            value => _playerRetaliationDuration = Math.Max(1f, value), true);
    }

    /// <summary>
    /// Alert visible allies of the victim once, without propagating the alert further.
    /// </summary>
    private void AlertNearbyAllies(EntityUid victim, EntityUid attacker)
    {
        if (_allyAssistRange <= 0f || !TryComp<NpcFactionMemberComponent>(victim, out var faction))
            return;

        _nearbyAllies.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(victim), _allyAssistRange, _nearbyAllies);
        foreach (var (ally, allyFaction) in _nearbyAllies)
        {
            if (ally == victim || HasComp<ActorComponent>(ally) ||
                !_npcFaction.IsEntityFriendly((victim, faction), (ally, allyFaction)) ||
                !_npcFaction.IsEntityFriendly((ally, allyFaction), (victim, faction)) ||
                !HasComp<ProximityNPCComponent>(ally) ||
                !TryComp<NPCRetaliationComponent>(ally, out var retaliation) ||
                !TryComp<MobStateComponent>(ally, out var state) ||
                state.CurrentState != MobState.Alive ||
                !_examine.InRangeUnOccluded(ally, victim, _allyAssistRange))
                continue;

            TryRetaliate((ally, retaliation), attacker);
        }
    }

    private void RememberAttackerAndReplan(Entity<NPCRetaliationComponent> ent, EntityUid target)
    {
        TimeSpan? memoryLength = HasComp<ActorComponent>(target) && HasComp<ProximityNPCComponent>(ent.Owner)
            ? TimeSpan.FromSeconds(_playerRetaliationDuration)
            : ent.Comp.AttackMemoryLength;

        if (memoryLength is {} duration)
        {
            ent.Comp.AttackMemories[target] = _timing.CurTime + duration;

            if (HasComp<ActorComponent>(target))
                _proximity.WakeForRetaliation(ent.Owner, _timing.CurTime + duration);
        }

        if (HasComp<ProximityNPCComponent>(ent.Owner) && TryComp<HTNComponent>(ent.Owner, out var htn))
            _htn.Replan(htn);
    }

    /// <summary>
    /// The most recent living player targeted by direct or ally retaliation.
    /// </summary>
    public bool TryGetPlayerAttacker(EntityUid uid, out EntityUid attacker)
    {
        attacker = default;
        if (!HasComp<ProximityNPCComponent>(uid) ||
            !TryComp<NPCRetaliationComponent>(uid, out var retaliation) ||
            !TryComp<TransformComponent>(uid, out var victimXform))
            return false;

        var latest = TimeSpan.Zero;
        foreach (var (candidate, until) in retaliation.AttackMemories)
        {
            if (until <= _timing.CurTime || until <= latest ||
                !HasComp<ActorComponent>(candidate) ||
                !TryComp<MobStateComponent>(candidate, out var state) ||
                state.CurrentState != MobState.Alive ||
                !TryComp<TransformComponent>(candidate, out var attackerXform) ||
                attackerXform.MapID != victimXform.MapID)
                continue;

            latest = until;
            attacker = candidate;
        }

        return attacker.IsValid();
    }

    /// <summary>
    /// Stop pursuing a player when pathfinding has proved the route unreachable.
    /// A later hit can start retaliation again.
    /// </summary>
    public bool ForgetUnreachableAttacker(EntityUid uid, EntityUid target)
    {
        if (!HasComp<ProximityNPCComponent>(uid) || !HasComp<ActorComponent>(target) ||
            !TryComp<NPCRetaliationComponent>(uid, out var retaliation) ||
            !retaliation.AttackMemories.Remove(target))
            return false;

        _npcFaction.DeAggroEntity(uid, target);
        if (retaliation.AttackMemories.Count == 0)
            _proximity.CancelRetaliation(uid);

        return true;
    }

    private void ClearExpiredAggression(Entity<NPCRetaliationComponent> ent, FactionExceptionComponent factionException)
    {
        _expired.Clear();
        foreach (var (entity, until) in ent.Comp.AttackMemories)
        {
            if (!TerminatingOrDeleted(entity) && _timing.CurTime < until)
                continue;

            _npcFaction.DeAggroEntity((ent.Owner, factionException), entity);
            _expired.Add(entity);
        }

        foreach (var entity in _expired)
            ent.Comp.AttackMemories.Remove(entity);
    }
}
