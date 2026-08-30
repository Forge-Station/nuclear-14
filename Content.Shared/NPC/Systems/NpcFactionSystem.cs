using Content.Shared.NPC.Components;
using Content.Shared.NPC.Events;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using System.Collections.Frozen;
using System.Linq;

namespace Content.Shared.NPC.Systems;

/// <summary>
///     Outlines faction relationships with each other.
///     part of psionics rework was making this a partial class. Should've already been handled upstream, based on the linter.
/// </summary>
public sealed partial class NpcFactionSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    /// <summary>
    /// To avoid prototype mutability we store an intermediary data class that gets used instead.
    /// </summary>
    private FrozenDictionary<string, FactionData> _factions = FrozenDictionary<string, FactionData>.Empty;

    /// <summary>
    /// Reused between range scans so per-NPC hostility/friendliness checks don't allocate a set each call.
    /// NPC updates run single-threaded on the cooperative HTN job queue, and this buffer is only used inside
    /// synchronous helpers (never held across a job suspension), so a shared field is safe here.
    /// </summary>
    private readonly HashSet<Entity<NpcFactionMemberComponent>> _nearbyMembers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcFactionMemberComponent, ComponentStartup>(OnFactionStartup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnProtoReload);

        InitializeException();
        InitializeCore();
        InitializeItems();
        RefreshFactions();
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (obj.WasModified<NpcFactionPrototype>())
            RefreshFactions();
    }

    private void OnFactionStartup(Entity<NpcFactionMemberComponent> ent, ref ComponentStartup args)
    {
        RefreshFactions(ent);
    }

    /// <summary>
    /// Refreshes the cached factions for this component.
    /// </summary>
    private void RefreshFactions(Entity<NpcFactionMemberComponent> ent)
    {
        ent.Comp.FriendlyFactions.Clear();
        ent.Comp.HostileFactions.Clear();

        foreach (var faction in ent.Comp.Factions)
        {
            // YAML Linter already yells about this, don't need to log an error here
            if (!_factions.TryGetValue(faction, out var factionData))
                continue;

            ent.Comp.FriendlyFactions.UnionWith(factionData.Friendly);
            ent.Comp.HostileFactions.UnionWith(factionData.Hostile);
        }
    }

    /// <summary>
    /// Returns whether an entity is a member of a faction.
    /// </summary>
    public bool IsMember(Entity<NpcFactionMemberComponent?> ent, string faction)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        return ent.Comp.Factions.Contains(faction);
    }

    /// <summary>
    /// Returns whether an entity is a member of any listed faction.
    /// If the list is empty this returns false.
    /// </summary>
    public bool IsMemberOfAny(Entity<NpcFactionMemberComponent?> ent, IEnumerable<ProtoId<NpcFactionPrototype>> factions)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        foreach (var faction in factions)
        {
            if (ent.Comp.Factions.Contains(faction))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds this entity to the particular faction.
    /// </summary>
    public void AddFaction(Entity<NpcFactionMemberComponent?> ent, string faction, bool dirty = true)
    {
        if (!_proto.HasIndex<NpcFactionPrototype>(faction))
        {
            Log.Error($"Unable to find faction {faction}");
            return;
        }

        ent.Comp ??= EnsureComp<NpcFactionMemberComponent>(ent);
        if (!ent.Comp.Factions.Add(faction))
            return;

        RaiseLocalEvent(ent.Owner, new NpcFactionAddedEvent(faction));

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    /// <summary>
    /// Adds this entity to the particular factions.
    /// </summary>
    public void AddFactions(Entity<NpcFactionMemberComponent?> ent, HashSet<ProtoId<NpcFactionPrototype>> factions, bool dirty = true)
    {
        ent.Comp ??= EnsureComp<NpcFactionMemberComponent>(ent);

        foreach (var faction in factions)
        {
            if (!_proto.HasIndex(faction))
            {
                Log.Error($"Unable to find faction {faction}");
                continue;
            }

            RaiseLocalEvent(ent.Owner, new NpcFactionAddedEvent(faction));

            ent.Comp.Factions.Add(faction);
        }

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    /// <summary>
    /// Removes this entity from the particular faction.
    /// </summary>
    public void RemoveFaction(Entity<NpcFactionMemberComponent?> ent, string faction, bool dirty = true)
    {
        if (!_proto.HasIndex<NpcFactionPrototype>(faction))
        {
            Log.Error($"Unable to find faction {faction}");
            return;
        }

        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!ent.Comp.Factions.Remove(faction))
            return;

        RaiseLocalEvent(ent.Owner, new NpcFactionRemovedEvent(faction));

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    /// <summary>
    /// Remove this entity from all factions.
    /// </summary>
    public void ClearFactions(Entity<NpcFactionMemberComponent?> ent, bool dirty = true)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Factions.Clear();

        if (dirty)
            RefreshFactions((ent, ent.Comp));
    }

    public IEnumerable<EntityUid> GetNearbyHostiles(Entity<NpcFactionMemberComponent?, FactionExceptionComponent?> ent, float range)
    {
        var results = new HashSet<EntityUid>();
        GetNearbyHostiles(ent, range, results);
        return results;
    }

    /// <summary>
    /// Non-allocating variant: clears <paramref name="results"/> and fills it with nearby hostiles.
    /// Behaviour matches the enumerable overload; avoids the LINQ/iterator allocations on the hot NPC path.
    /// </summary>
    public void GetNearbyHostiles(Entity<NpcFactionMemberComponent?, FactionExceptionComponent?> ent, float range, HashSet<EntityUid> results)
    {
        results.Clear();

        if (!Resolve(ent, ref ent.Comp1, false))
            return;

        // Nearby members of our hostile factions, minus anything we also count as friendly:
        // having both a hostile faction and a shared faction must not be strictly negative.
        GetNearbyFactions(ent.Owner, range, ent.Comp1.HostileFactions, results);
        var self = (ent.Owner, ent.Comp1);
        results.RemoveWhere(target => IsEntityFriendly(self, target));

        if (!Resolve(ent, ref ent.Comp2, false))
            return;

        // Add explicit per-entity hostiles, then drop anything we are told to ignore.
        var faction = (ent.Owner, ent.Comp2);
        foreach (var hostile in GetHostiles(faction))
            results.Add(hostile);

        results.RemoveWhere(target => IsIgnored(faction, target));
    }

    public IEnumerable<EntityUid> GetNearbyFriendlies(Entity<NpcFactionMemberComponent?> ent, float range)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return Array.Empty<EntityUid>();

        var results = new HashSet<EntityUid>();
        GetNearbyFactions(ent.Owner, range, ent.Comp.FriendlyFactions, results);
        return results;
    }

    /// <summary>
    /// Adds nearby entities sharing any of <paramref name="factions"/> into <paramref name="results"/>.
    /// Uses a reused member-query buffer instead of allocating a lookup set per call.
    /// </summary>
    private void GetNearbyFactions(EntityUid entity, float range, HashSet<ProtoId<NpcFactionPrototype>> factions, HashSet<EntityUid> results)
    {
        var xform = Transform(entity);

        _nearbyMembers.Clear();
        _lookup.GetEntitiesInRange(_xform.GetMapCoordinates((entity, xform)), range, _nearbyMembers);

        foreach (var member in _nearbyMembers)
        {
            if (member.Owner == entity)
                continue;

            if (!factions.Overlaps(member.Comp.Factions))
                continue;

            results.Add(member.Owner);
        }
    }

    /// <remarks>
    /// 1-way and purely faction based, ignores faction exception.
    /// </remarks>
    public bool IsEntityFriendly(Entity<NpcFactionMemberComponent?> ent, Entity<NpcFactionMemberComponent?> other)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !Resolve(other, ref other.Comp, false))
            return false;

        // Single pass over the shared factions (those both ent and other belong to). Avoids the LINQ
        // Intersect allocation and the previous double-enumeration (foreach + Count() re-ran it).
        var shared = false;
        foreach (var faction in ent.Comp.Factions)
        {
            if (!other.Comp.Factions.Contains(faction))
                continue;

            shared = true;
            if (_factions[faction].IsHostileToSelf)
                return false;
        }

        return shared || ent.Comp.FriendlyFactions.Overlaps(other.Comp.Factions);
    }

    public bool IsFactionFriendly(string target, string with)
    {
        return _factions[target].Friendly.Contains(with) && _factions[with].Friendly.Contains(target);
    }

    public bool IsFactionFriendly(string target, Entity<NpcFactionMemberComponent?> with)
    {
        if (!Resolve(with, ref with.Comp, false))
            return false;

        return with.Comp.Factions.All(x => IsFactionFriendly(target, x)) ||
               with.Comp.FriendlyFactions.Contains(target);
    }

    public bool IsFactionHostile(string target, string with)
    {
        return _factions[target].Hostile.Contains(with) && _factions[with].Hostile.Contains(target);
    }

    public bool IsFactionHostile(string target, Entity<NpcFactionMemberComponent?> with)
    {
        if (!Resolve(with, ref with.Comp, false))
            return false;

        return with.Comp.Factions.All(x => IsFactionHostile(target, x)) ||
               with.Comp.HostileFactions.Contains(target);
    }

    public bool IsFactionNeutral(string target, string with)
    {
        return !IsFactionFriendly(target, with) && !IsFactionHostile(target, with);
    }

    /// <summary>
    /// Makes the source faction friendly to the target faction, 1-way.
    /// </summary>
    public void MakeFriendly(string source, string target)
    {
        if (!_factions.TryGetValue(source, out var sourceFaction))
        {
            Log.Error($"Unable to find faction {source}");
            return;
        }

        if (!_factions.ContainsKey(target))
        {
            Log.Error($"Unable to find faction {target}");
            return;
        }

        sourceFaction.Friendly.Add(target);
        sourceFaction.Hostile.Remove(target);
        RefreshFactions();
    }

    /// <summary>
    /// Makes the source faction hostile to the target faction, 1-way.
    /// </summary>
    public void MakeHostile(string source, string target)
    {
        if (!_factions.TryGetValue(source, out var sourceFaction))
        {
            Log.Error($"Unable to find faction {source}");
            return;
        }

        if (!_factions.ContainsKey(target))
        {
            Log.Error($"Unable to find faction {target}");
            return;
        }

        sourceFaction.Friendly.Remove(target);
        sourceFaction.Hostile.Add(target);
        RefreshFactions();
    }

    private void RefreshFactions()
    {
        _factions = _proto.EnumeratePrototypes<NpcFactionPrototype>().ToFrozenDictionary(
            faction => faction.ID,
            faction => new FactionData
            {
                IsHostileToSelf = faction.Hostile.Contains(faction.ID),
                Friendly = faction.Friendly.ToHashSet(),
                Hostile = faction.Hostile.ToHashSet()
            });

        var query = AllEntityQuery<NpcFactionMemberComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.FriendlyFactions.Clear();
            comp.HostileFactions.Clear();
            RefreshFactions((uid, comp));
        }
    }
}
