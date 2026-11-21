// Forge-Change: wake damaged NPCs, pursue attackers, and alert nearby visible allies.
using Content.Server.NPC.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared._Forge.NPC;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Handles NPC which become aggressive after being attacked.
/// </summary>
public sealed partial class NPCRetaliationSystem : EntitySystem // Forge-Change: helpers live in _Forge.
{
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    /// <inheritdoc />
    public override void Initialize()
    {
        InitializeForgeRetaliation(); // Forge-Change

        SubscribeLocalEvent<NPCRetaliationComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<NPCRetaliationComponent, DisarmedEvent>(OnDisarmed);
    }

    private void OnDamageChanged(Entity<NPCRetaliationComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (args.Origin is not {} origin)
            return;

        // Forge-Change-Start: a direct player hit alerts visible allies within the configured radius.
        if (TryRetaliate(ent, origin) &&
            HasComp<ProximityNPCComponent>(ent.Owner) &&
            HasComp<ActorComponent>(origin))
            AlertNearbyAllies(ent.Owner, origin);
        // Forge-Change-End
    }

    private void OnDisarmed(Entity<NPCRetaliationComponent> ent, ref DisarmedEvent args)
    {
        TryRetaliate(ent, args.Source);
    }

    public bool TryRetaliate(Entity<NPCRetaliationComponent> ent, EntityUid target)
    {
        // don't retaliate against inanimate objects.
        if (!HasComp<MobStateComponent>(target))
            return false;

        // Forge-Change: direct attacks by players provoke retaliation even with friendly faction status.
        if ((!HasComp<ActorComponent>(target) || !HasComp<ProximityNPCComponent>(ent.Owner)) &&
            !ent.Comp.RetaliateFriendlies
            && _npcFaction.IsEntityFriendly(ent.Owner, target))
            return false;

        _npcFaction.AggroEntity(ent.Owner, target);
        RememberAttackerAndReplan(ent, target); // Forge-Change

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NPCRetaliationComponent, FactionExceptionComponent>();
        while (query.MoveNext(out var uid, out var retaliationComponent, out var factionException))
        {
            ClearExpiredAggression((uid, retaliationComponent), factionException); // Forge-Change
        }
    }
}
