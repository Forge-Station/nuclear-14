using Content.Server.GameTicking;
using Content.Shared._Forge.WorldTime;
using Content.Shared._NC14.DayNightCycle;
using Content.Shared.Light.Components;
using Robust.Shared.Map;

namespace Content.Server._Forge.WorldTime;

// Marks the main map as the single source of time-of-day readings for the round.
// Additional maps never replace this source.
public sealed class WorldTimeSourceSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        ClearExistingSources();

        var mapId = _gameTicker.DefaultMap;

        if (mapId == MapId.Nullspace
            || !_mapManager.MapExists(mapId))
        {
            Log.Warning("Unable to select the world time source: the default map does not exist.");
            return;
        }

        var mapUid = _mapManager.GetMapEntityId(mapId);

        if (!HasComp<DayNightCycleComponent>(mapUid)
            && !HasComp<LightCycleComponent>(mapUid))
        {
            Log.Warning(
                $"Unable to select {ToPrettyString(mapUid)} as the world time source: " +
                "it has no supported light cycle.");
            return;
        }

        EnsureComp<WorldTimeSourceComponent>(mapUid).RoundStartTime = _gameTicker.RoundStartTimeSpan;
        EnsureComp<WorldTimeSettingsComponent>(mapUid);
    }

    private void ClearExistingSources()
    {
        var sources = new List<EntityUid>();

        var query = EntityQueryEnumerator<WorldTimeSourceComponent>();

        while (query.MoveNext(out var uid, out _))
            sources.Add(uid);

        foreach (var source in sources)
            RemComp<WorldTimeSourceComponent>(source);
    }
}
