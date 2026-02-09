using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server.GameTicking;
using Content.Server.StationEvents.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Components;
using Content.Shared.Random.Helpers;
using Robust.Shared.Utility;
using Content.Shared.Salvage;
using System.Linq;

namespace Content.Server.StationEvents.Events;

public sealed class PirateRadioSpawnRule : StationEventSystem<PirateRadioSpawnRuleComponent>
{
    [Dependency] private readonly MapLoaderSystem _map = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IConfigurationManager _confMan = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;

    protected override void Started(EntityUid uid, PirateRadioSpawnRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var station = _gameTicker.GetSpawnableStations();
        if (station is null)
            return;
        var stationGrids = new HashSet<EntityUid>();
        foreach (var stations in station)
        {
            if (TryComp<StationDataComponent>(stations, out var data) && _station.GetLargestGrid(data) is { } grid)
                stationGrids.Add(grid);
        }

        // _random forces Test Fails if given an empty list. which is guaranteed to happen during Tests.
        if (stationGrids.Count <= 0)
            return;

        var targetStation = _random.Pick(stationGrids);
        var targetMapId = Transform(targetStation).MapID;

        if (!_mapSystem.MapExists(targetMapId))
            return;

        var randomOffset = _random.NextVector2(component.MinimumDistance, component.MaximumDistance);
        var offset = _xform.GetWorldPosition(targetStation) + randomOffset;

        if (!_map.TryLoadGrid(Transform(targetStation).MapID, new ResPath(_random.Pick(component.PirateRadioShuttlePath)), out var outpostGrid, offset: offset))
            return;

        SpawnDebris(component, new List<EntityUid> { outpostGrid.Value.Owner });
    }

    private void SpawnDebris(PirateRadioSpawnRuleComponent component, IReadOnlyList<EntityUid> outpostids)
    {
        if (_confMan.GetCVar(CCVars.WorldgenEnabled)
            || component.DebrisCount <= 0)
            return;

        foreach (var id in outpostids)
        {
            var outpostaabb = _xform.GetWorldPosition(id);
            var k = 0;

            while (k < component.DebrisCount)
            {
                var debrisRandomOffset = _random.NextVector2(component.MinimumDebrisDistance, component.MaximumDebrisDistance);
                var randomer = _random.NextVector2(component.DebrisMinimumOffset, component.DebrisMaximumOffset); //Second random vector to ensure the outpost isn't perfectly centered in the debris field
                var offset = outpostaabb + debrisRandomOffset + randomer;

                var salvPrototypes = _prototypeManager.EnumeratePrototypes<SalvageMapPrototype>().ToList();
                var salvageProto = _random.Pick(salvPrototypes);

                if (!_mapSystem.MapExists(GameTicker.DefaultMap))
                    return;

                // Round didn't start before running this, leading to a null-space test fail.
                if (GameTicker.DefaultMap == MapId.Nullspace)
                    return;

                if (!_map.TryLoadGrid(GameTicker.DefaultMap, salvageProto.MapPath, out _, offset: offset))
                    return;

                k++;
            }
        }
    }

    protected override void Ended(EntityUid uid, PirateRadioSpawnRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        if (component.AdditionalRule != null)
            GameTicker.EndGameRule(component.AdditionalRule.Value);
    }
}
