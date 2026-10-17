using Content.Server.StationEvents.Components;
using Content.Shared.GameTicking.Components;
using JetBrains.Annotations;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Content.Server.Announcements.Systems;
using Content.Server.GameTicking;
using Content.Shared.Emag.Systems;
using Robust.Shared.Timing;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Station.Systems;
using Content.Shared.Station.Components;

namespace Content.Server.StationEvents.Events;

[UsedImplicitly]
public sealed class AirlockVirusRule : StationEventSystem<AirlockVirusRuleComponent>
{
    [Dependency] private readonly AnnouncerSystem _announcer = default!;
    [Dependency] private readonly ServerGameTicker _gameTicker = default!; // Forge-Change
    [Dependency] private readonly EmagSystem _emag = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _station = default!;

    protected override void Started(EntityUid uid, AirlockVirusRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var station = _gameTicker.GetSpawnableStations();
        if (station is null)
            return;
        var stationGrids = new HashSet<EntityUid>();
        foreach (var stations in station)
        {
            if (TryComp<StationDataComponent>(stations, out var data) && _station.GetLargestGrid((stations, data)) is { } grid)
                stationGrids.Add(grid);
        }

        var query = EntityManager.EntityQueryEnumerator<AirlockVirusTargetComponent>();
        while (query.MoveNext(out var airlockUid, out var _))
        {
            var parent = Transform(airlockUid).GridUid;
            if (parent is null
                || !stationGrids.Contains(parent!.Value))
                continue;

            Timer.Spawn(TimeSpan.FromSeconds(_random.NextDouble(component.MinimumTimeToEmag, component.MaximumTimeToEmag)), () =>
                _emag.TryEmagEffect(uid, uid, airlockUid)); // Forge-Change: ЭМИ-эффект теперь TryEmagEffect
        }

        _announcer.SendAnnouncement(
            _announcer.GetAnnouncementId(args.RuleId ?? string.Empty), // Forge-Change: идентификатор правила стал необязательным
            Filter.Broadcast(),
            "airlock-virus-event-announcement",
            null,
            Color.FromHex("#18abf5"),
            null, null);
    }
}
