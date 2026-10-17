using Content.Server.DeltaV.ParadoxAnomaly.Components;
using Content.Shared.DetailExaminable;
using Content.Server.GenericAntag;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Psionics;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Station.Systems;
using Content.Shared.Spawners.Components;
using Content.Shared.Body;
using Content.Shared.Roles.Components;

namespace Content.Server.DeltaV.ParadoxAnomaly.Systems;

/// <summary>
/// 90% of the work is done by exterminator since its a reskin.
/// All the logic here is spawning since thats tricky.
/// </summary>
public sealed class ParadoxAnomalySystem : EntitySystem
{
    [Dependency] private readonly GenericAntagSystem _genericAntag = default!;
    [Dependency] private readonly GhostRoleSystem _ghostRole = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly PsionicsSystem _psionics = default!;
    [Dependency] private readonly HumanoidProfileSystem _humanoid = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly SharedVisualBodySystem _visualBody = default!; // Forge-Change
    [Dependency] private readonly SharedJobSystem _jobs = default!; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ParadoxAnomalySpawnerComponent, TakeGhostRoleEvent>(OnTakeGhostRole);
    }

    private void OnTakeGhostRole(Entity<ParadoxAnomalySpawnerComponent> ent, ref TakeGhostRoleEvent args)
    {
        Log.Info($"Using paradox anomaly spawner {ent}");
        if (!TrySpawnParadoxAnomaly(ent.Comp.Rule, out var twin))
            return;

        Log.Info($"Created paradox anomaly {ToPrettyString(twin):twin}");
        var role = Comp<GhostRoleComponent>(ent);
        _ghostRole.GhostRoleInternalCreateMindAndTransfer(args.Player, ent, twin.Value, role);
        _ghostRole.UnregisterGhostRole((ent.Owner, role));

        args.TookRole = true;
        QueueDel(ent);
    }

    private bool TrySpawnParadoxAnomaly(string rule, [NotNullWhen(true)] out EntityUid? twin)
    {
        twin = null;

        // Forge-Change: профиль персонажа апстрим на сущности больше не хранит, поэтому
        // близнец собирается из самого тела: вид и внешность копируются с оригинала.
        var candidates = new List<(EntityUid, EntityUid, SpeciesPrototype)>();
        var query = EntityQueryEnumerator<MindContainerComponent, HumanoidProfileComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var humanoid))
        {
            if (!_proto.TryIndex<SpeciesPrototype>(humanoid.Species, out var species))
                continue;

            if (_mind.GetMind(uid, mindContainer) is not {} mindId
                || !_role.MindHasRole<JobRoleComponent>(mindId, out _))
                continue;

            if (_role.MindIsAntagonist(mindId))
                continue;

            // TODO: when metempsychosis real skip whoever has Karma

            candidates.Add((uid, mindId, species));
        }

        twin = SpawnParadoxAnomaly(candidates, rule);
        return twin != null;
    }

    private EntityUid? SpawnParadoxAnomaly(List<(EntityUid, EntityUid, SpeciesPrototype)> candidates, string rule)
    {
        // Select a candidate.
        if (candidates.Count == 0)
            return null;

        var (uid, mindId, species) = _random.Pick(candidates);
        if (!_jobs.MindTryGetJob(mindId, out var job)) // Forge-Change
            return null;

        // Find a suitable spawn point.
        var station = _station.GetOwningStation(uid);
        var latejoins = new List<EntityUid>();
        var query = EntityQueryEnumerator<SpawnPointComponent>();
        while (query.MoveNext(out var spawnUid, out var spawnPoint))
        {
            if (spawnPoint.SpawnType != SpawnPointType.LateJoin)
                continue;

            if (_station.GetOwningStation(spawnUid) == station)
                latejoins.Add(spawnUid);
        }

        if (latejoins.Count == 0)
            return null;

        // Spawn the twin.
        var destination = Transform(_random.Pick(latejoins)).Coordinates;
        var spawned = Spawn(species.Prototype, destination);

        // Set the kill target to the chosen player
        // _terminator.SetTarget(spawned, mindId);
        _genericAntag.MakeAntag(spawned, rule);

        //////////////////////////
        //    /!\ WARNING /!\   //
        // MAJOR SHITCODE BELOW //
        //    /!\ WARNING /!\   //
        //////////////////////////

        // Copy the details.
        // Forge-Change: профиль берём с самого оригинала, внешность - через Nubody.
        if (TryComp<HumanoidProfileComponent>(uid, out var sourceProfile))
        {
            var twinProfile = EnsureComp<HumanoidProfileComponent>(spawned);
            twinProfile.Gender = sourceProfile.Gender;
            twinProfile.Age = sourceProfile.Age;
            twinProfile.Species = sourceProfile.Species;
            twinProfile.Sex = sourceProfile.Sex;
            twinProfile.Voice = sourceProfile.Voice;
            Dirty(spawned, twinProfile);
        }

        _visualBody.CopyAppearanceFrom(uid, spawned);
        _metaData.SetEntityName(spawned, Name(uid));

        if (TryComp<DetailExaminableComponent>(uid, out var detail))
        {
            var detailCopy = EnsureComp<DetailExaminableComponent>(spawned);
            detailCopy.Content = detail.Content;
        }

        if (job.StartingGear != null && _proto.TryIndex<StartingGearPrototype>(job.StartingGear, out var gear))
        {
            _stationSpawning.EquipStartingGear(spawned, gear);
            // Forge-Change: EquipIdCard стал SetPdaAndIdCardData; форковый набор снаряжения
            // близнецу не выдаётся - для него нужен профиль, которого у нас больше нет.
            _stationSpawning.SetPdaAndIdCardData(spawned, Name(uid), job, station);
        }

        foreach (var special in job.Special)
        {
            special.AfterEquip(spawned);
        }

        // TODO: In a future PR, make it so that the Paradox Anomaly spawns with a completely 1:1 clone of the victim's entire PsionicComponent.
        if (HasComp<PsionicComponent>(uid))
            EnsureComp<PsionicComponent>(spawned);

        return spawned;
    }
}
