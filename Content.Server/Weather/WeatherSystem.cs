using Content.Server.Administration;
/// Forge-Change
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Maps;
/// Forge-Change-Start
using Content.Server.Radiation.Components;
using Content.Server.Radiation.Systems;
/// Forge-Change-End
using Content.Shared.Administration;
/// Forge-Change
using Content.Shared._N14.Weather;
using Content.Shared.CCVar;
/// Forge-Change-Start
using Content.Shared.Ghost;
using Content.Shared.GameTicking;
using Content.Shared.Light.Components;
/// Forge-Change-End
using Content.Shared.Weather;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
/// Forge-Change-Start
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
/// Forge-Change-End
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.Weather;

public sealed class WeatherSystem : SharedWeatherSystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IConsoleHost _console = default!;
    /// Forge-Change-Del [Dependency] private readonly IMapManager _map = default!;
    /// Forge-Change
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    /// Forge-Change
    [Dependency] private readonly RadiationSystem _radiation = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    /// Forge-Change-Del [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    /// Forge-Change-Start
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const float WeatherEffectInterval = 1f;
    private const string DefaultWeatherPrototype = "Default";
    // Schedule the next event only after the previous weather has fully ended.
    private static readonly TimeSpan RandomWeatherMinDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RandomWeatherMaxDelay = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan RandomWeatherMinDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RandomWeatherMaxDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RandomRadioactiveWeatherMinDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RandomRadioactiveWeatherMaxDuration = TimeSpan.FromMinutes(3);

    private readonly Dictionary<(EntityUid MapUid, string ProtoId), float> _effectAccumulators = new();
    private readonly Dictionary<MapId, GameMapPrototype> _loadedGameMaps = new();
    private TimeSpan? _nextRandomWeatherTime;
    /// Forge-Change-End

    public override void Initialize()
    {
        base.Initialize();
        /// Forge-Change-Start
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PostGameMapLoad>(OnGameMapLoaded);
        /// Forge-Change-End
        SubscribeLocalEvent<WeatherComponent, ComponentGetState>(OnWeatherGetState);
        _console.RegisterCommand("weather",
            Loc.GetString("cmd-weather-desc"),
            Loc.GetString("cmd-weather-help"),
            WeatherTwo,
            WeatherCompletion);
        _console.RegisterCommand("randomweather",
            Loc.GetString("cmd-randomweather-desc"),
            Loc.GetString("cmd-randomweather-help"),
            RandomWeatherCommand);
        /// Forge-Change-Start
        _console.RegisterCommand("nextweather",
            Loc.GetString("cmd-nextweather-desc"),
            Loc.GetString("cmd-nextweather-help"),
            NextWeatherCommand);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _nextRandomWeatherTime = null;
        _effectAccumulators.Clear();
        _loadedGameMaps.Clear();
    }

    private void OnGameMapLoaded(PostGameMapLoad ev)
    {
        _loadedGameMaps[ev.Map] = ev.GameMap;
        /// Forge-Change-End
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        /// Forge-Change-Del if (_config.GetCVar(CCVars.AutoWeather) && _gameTicker.RunLevel == GameRunLevel.InRound && !WeatherRunning())
        /// Forge-Change
        if (!_config.GetCVar(CCVars.AutoWeather) || _gameTicker.RunLevel != GameRunLevel.InRound)
        {
            /// Forge-Change-Start
            _nextRandomWeatherTime = null;
            return;
        }

        if (WeatherEventRunning())
        {
            _nextRandomWeatherTime = null;
            return;
        }

        if (_nextRandomWeatherTime == null)
        {
            ScheduleNextRandomWeather();
            return;
        }

        if (Timing.CurTime >= _nextRandomWeatherTime.Value)
        {
            _nextRandomWeatherTime = null;
            /// Forge-Change-End
            var (weather, map) = SetRandomWeather();

            if (weather != null)
            {
                /// Forge-Change-Del Logger.InfoS("weather", $"Randomizing weather to {weather.ID} on map {map}");
                /// Forge-Change-Start
                Log.Info($"Randomizing weather to {weather.ID} on map {map}");
            }
            else
            {
                ScheduleNextRandomWeather();
                /// Forge-Change-End
            }
        }
    }

    /// Forge-Change-Del private bool WeatherRunning()
    /// Forge-Change-Start
    protected override void Run(EntityUid uid, WeatherData weather, WeatherPrototype weatherProto, float frameTime)
    {
        base.Run(uid, weather, weatherProto, frameTime);

        if (weather.State != WeatherState.Running)
            return;

        if (weatherProto.Radioactive)
            RunRadioactiveWeather(uid, weatherProto, frameTime);
    }

    protected override bool SetState(EntityUid uid, WeatherState state, WeatherComponent component, WeatherData weather, WeatherPrototype weatherProto)
    {
        if (!base.SetState(uid, state, component, weather, weatherProto))
            return false;

        if (state == WeatherState.Starting && weatherProto.ShowMessage && weatherProto.Message != string.Empty)
        {
            var sender = weatherProto.Sender != null
                ? Loc.GetString(weatherProto.Sender.Value)
                : Loc.GetString("weather-announcement");
            var color = weatherProto.Radioactive ? Color.Red : Color.LightSkyBlue;

            _chat.DispatchGlobalAnnouncement(Loc.GetString(weatherProto.Message), sender, colorOverride: color);
        }

        return true;
    }

    protected override void EndWeather(EntityUid uid, WeatherComponent component, string proto)
    {
        _effectAccumulators.Remove((uid, proto));
        base.EndWeather(uid, component, proto);
    }

    public bool CanSeeThroughWeather(EntityUid viewer, EntityUid target)
    {
        if (!TryComp(viewer, out TransformComponent? viewerXform) ||
            !TryComp(target, out TransformComponent? targetXform))
        {
            return true;
        }

        if (viewerXform.MapUid == null ||
            viewerXform.MapUid != targetXform.MapUid ||
            !TryComp<WeatherComponent>(viewerXform.MapUid.Value, out var weather))
        {
            return true;
        }

        var visibilityRadius = float.MaxValue;
        foreach (var (protoId, data) in weather.Weather)
        {
            if (data.State != WeatherState.Starting && data.State != WeatherState.Running)
                continue;

            if (!_prototype.TryIndex<WeatherPrototype>(protoId, out var weatherProto) ||
                weatherProto.VisibilityClearRadius <= 0f)
            {
                continue;
            }

            visibilityRadius = MathF.Min(visibilityRadius, weatherProto.VisibilityClearRadius);
        }

        if (visibilityRadius == float.MaxValue)
            return true;

        if (!IsWeatherExposed(viewerXform.MapUid.Value, viewerXform) &&
            !IsWeatherExposed(viewerXform.MapUid.Value, targetXform))
        {
            return true;
        }

        if (!viewerXform.Coordinates.TryDistance(EntityManager, _transform, targetXform.Coordinates, out var distance))
            return true;

        return distance <= visibilityRadius;
    }

    private void RunRadioactiveWeather(EntityUid mapUid, WeatherPrototype weatherProto, float frameTime)
    {
        var key = (mapUid, weatherProto.ID);
        _effectAccumulators.TryGetValue(key, out var accumulator);
        accumulator += frameTime;

        if (accumulator < WeatherEffectInterval)
        {
            _effectAccumulators[key] = accumulator;
            return;
        }

        _effectAccumulators[key] = 0f;

        var irradiated = false;
        var query = EntityQueryEnumerator<RadiationReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var receiver, out var xform))
        {
            if (HasComp<GhostComponent>(uid) || HasComp<WeatherImmuneComponent>(uid))
                continue;

            if (!IsWeatherExposed(mapUid, xform))
                continue;

            _radiation.IrradiateReceiver((uid, receiver), weatherProto.RadsPerSecond, accumulator);
            irradiated = true;
        }

        if (irradiated)
            _radiation.RaiseRadiationUpdated();
    }

    private bool IsWeatherExposed(EntityUid mapUid, TransformComponent xform)
    {
        if (xform.MapUid != mapUid || xform.GridUid == null)
            return false;

        var gridUid = xform.GridUid.Value;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        TryComp<RoofComponent>(gridUid, out var roof);
        var tile = MapManager.GetTileRef(gridUid, grid, xform.Coordinates);
        return CanWeatherAffect(gridUid, grid, tile, roof);
    }

    private bool WeatherEventRunning()
    /// Forge-Change-End
    {
        var query = EntityQueryEnumerator<WeatherComponent>();
        /// Forge-Change-Del while (query.MoveNext(out var uid, out var comp))
        /// Forge-Change
        while (query.MoveNext(out _, out var comp))
        {
            /// Forge-Change-Del if (comp.Weather.Count > 0)
            /// Forge-Change
            foreach (var (protoId, _) in comp.Weather)
            {
                /// Forge-Change-Start
                if (protoId == DefaultWeatherPrototype)
                    continue;

                /// Forge-Change-End
                return true;
            }
        }
        return false;
    }

    private void OnWeatherGetState(EntityUid uid, WeatherComponent component, ref ComponentGetState args)
    {
        args.State = new WeatherComponentState(component.Weather);
    }

    [AdminCommand(AdminFlags.Fun)]
    private void WeatherTwo(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Loc.GetString("cmd-weather-error-no-arguments"));
            return;
        }

        if (!int.TryParse(args[0], out var mapInt))
            return;

        var mapId = new MapId(mapInt);

        if (!MapManager.MapExists(mapId))
            return;

        /// Forge-Change-Del if (!_mapSystem.TryGetMap(mapId, out var mapUid))
        /// Forge-Change
        if (!MapManager.TryGetMap(mapId, out var mapUid))
            return;

        var weatherComp = EnsureComp<WeatherComponent>(mapUid.Value);

        //Weather Proto parsing
        WeatherPrototype? weather = null;
        if (!args[1].Equals("null"))
        {
            if (!ProtoMan.TryIndex(args[1], out weather))
            {
                shell.WriteError(Loc.GetString("cmd-weather-error-unknown-proto"));
                return;
            }
        }

        //Time parsing
        TimeSpan? endTime = null;
        if (args.Length == 3)
        {
            var curTime = Timing.CurTime;
            if (int.TryParse(args[2], out var durationInt))
            {
                endTime = curTime + TimeSpan.FromSeconds(durationInt);
            }
            else
            {
                shell.WriteError(Loc.GetString("cmd-weather-error-wrong-time"));
            }
        }

        SetWeather(mapId, weather, endTime);
    }

    [AdminCommand(AdminFlags.Fun)]
    private void RandomWeatherCommand(IConsoleShell shell, string argStr, string[] args)
    {
        var (weather, map) = SetRandomWeather();
        if (weather != null)
        {
            shell.WriteLine($"Picked {weather.ID} to run on map {map}");
        }
    }

    /// Forge-Change-Start
    [AdminCommand(AdminFlags.Admin)]
    private void NextWeatherCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_config.GetCVar(CCVars.AutoWeather))
        {
            shell.WriteLine(Loc.GetString("cmd-nextweather-auto-disabled"));
            return;
        }

        if (_gameTicker.RunLevel != GameRunLevel.InRound)
        {
            shell.WriteLine(Loc.GetString("cmd-nextweather-not-in-round"));
            return;
        }

        if (TryWriteActiveWeather(shell))
            return;

        if (_nextRandomWeatherTime == null)
            ScheduleNextRandomWeather();

        if (_nextRandomWeatherTime is not { } nextWeatherTime)
        {
            shell.WriteLine(Loc.GetString("cmd-nextweather-not-scheduled"));
            return;
        }

        var remaining = nextWeatherTime - Timing.CurTime;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        shell.WriteLine(Loc.GetString("cmd-nextweather-scheduled",
            ("time", FormatWeatherTime(remaining))));
    }

    private bool TryWriteActiveWeather(IConsoleShell shell)
    {
        var found = false;
        var query = EntityQueryEnumerator<WeatherComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            foreach (var (protoId, data) in comp.Weather)
            {
                if (protoId == DefaultWeatherPrototype)
                    continue;

                found = true;
                var ending = data.EndTime == null
                    ? Loc.GetString("cmd-nextweather-active-indefinite")
                    : Loc.GetString("cmd-nextweather-active-ending",
                        ("time", FormatWeatherTime(data.EndTime.Value - Timing.CurTime)));

                shell.WriteLine(Loc.GetString("cmd-nextweather-active",
                    ("weather", protoId),
                    ("state", data.State),
                    ("ending", ending)));
            }
        }

        if (found)
            shell.WriteLine(Loc.GetString("cmd-nextweather-active-note"));

        return found;
    }

    /// Forge-Change-End
    private (WeatherPrototype?, MapId) SetRandomWeather()
    {
        /// Forge-Change-Del var weather = RandomWeather();
        /// Forge-Change-Del if (weather != null) {
            /// Forge-Change-Del MapId map = GetMainMap();
            /// Forge-Change-Del SetWeather(map, weather, null);
        /// Forge-Change-Start
        var map = GetMainMap();
        if (map == MapId.Nullspace)
            return (null, map);

        _loadedGameMaps.TryGetValue(map, out var gameMap);
        var weather = RandomWeather(gameMap);
        if (weather != null)
        {
            SetWeather(map, weather, GetRandomWeatherEndTime(weather));
        /// Forge-Change-End
            return (weather, map);
        }
        return (weather, MapId.Nullspace);
    }

    /// Forge-Change-Del /**
     /// Forge-Change-Del * Try to guess the main map on which weather effects should be applied.
     /// Forge-Change-Del */
    /// Forge-Change-Start
    private void ScheduleNextRandomWeather()
    {
        var delay = _random.NextFloat(
            (float) RandomWeatherMinDelay.TotalSeconds,
            (float) RandomWeatherMaxDelay.TotalSeconds);

        _nextRandomWeatherTime = Timing.CurTime + TimeSpan.FromSeconds(delay);
    }

    private TimeSpan GetRandomWeatherEndTime(WeatherPrototype weather)
    {
        var maxDuration = weather.Radioactive
            ? RandomRadioactiveWeatherMaxDuration
            : RandomWeatherMaxDuration;

        var minDuration = weather.Radioactive
            ? RandomRadioactiveWeatherMinDuration
            : RandomWeatherMinDuration;

        var duration = _random.NextFloat(
            (float) minDuration.TotalSeconds,
            (float) maxDuration.TotalSeconds);

        if (weather.Duration > 0f)
            duration = MathF.Min(duration, weather.Duration);

        return Timing.CurTime + TimeSpan.FromSeconds(duration);
    }

    /// <summary>
    /// Prefer the round's main map, falling back to the first map for standalone maps.
    /// </summary>
    /// Forge-Change-End
    private MapId GetMainMap()
    {
        /// Forge-Change-Del foreach (var mapId in _map.GetAllMapIds().OrderBy(id => id.GetHashCode()))
        /// Forge-Change-Start
        if (_gameTicker.DefaultMap != MapId.Nullspace && MapManager.MapExists(_gameTicker.DefaultMap))
            return _gameTicker.DefaultMap;

        foreach (var mapId in MapManager.GetAllMapIds().OrderBy(id => id.GetHashCode()))
        /// Forge-Change-End
        {
            return mapId;
        }
        return MapId.Nullspace;
    }

    /// Forge-Change-Del private WeatherPrototype? RandomWeather()
    /// Forge-Change
    private WeatherPrototype? RandomWeather(GameMapPrototype? gameMap)
    {
        int totalChance = 0;
        foreach (var proto in _prototype.EnumeratePrototypes<WeatherPrototype>())
        {
            /// Forge-Change-Del totalChance += proto.Chance;
            /// Forge-Change-Start
            var weight = gameMap?.GetWeatherWeight(proto) ?? Math.Max(0, proto.Chance);
            if (weight <= 0)
                continue;

            totalChance += weight;
            /// Forge-Change-End
        }
        /// Forge-Change-Start

        if (totalChance <= 0)
            return null;
        /// Forge-Change-End

        int tgtChance = _random.Next(totalChance);
        int curr = 0;
        foreach (var proto in _prototype.EnumeratePrototypes<WeatherPrototype>())
        {
            /// Forge-Change-Del if (curr <= tgtChance && tgtChance < curr + proto.Chance)
            /// Forge-Change-Start
            var weight = gameMap?.GetWeatherWeight(proto) ?? Math.Max(0, proto.Chance);
            if (weight <= 0)
                continue;

            if (curr <= tgtChance && tgtChance < curr + weight)
            /// Forge-Change-End
                return proto;
            /// Forge-Change-Del curr += proto.Chance;
            /// Forge-Change
            curr += weight;
        }
        return null;
    /// Forge-Change-Start
    }

    private static string FormatWeatherTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        if (time.TotalHours >= 1)
            return $"{(int) time.TotalHours}h {time.Minutes}m {time.Seconds}s";

        if (time.TotalMinutes >= 1)
            return $"{time.Minutes}m {time.Seconds}s";

        return $"{time.Seconds}s";
    /// Forge-Change-End
    }

    private CompletionResult WeatherCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.MapIds(EntityManager), "Map Id");

        var a = CompletionHelper.PrototypeIDs<WeatherPrototype>(true, ProtoMan);
        var b = a.Concat(new[] { new CompletionOption("null", Loc.GetString("cmd-weather-null")) });
        return CompletionResult.FromHintOptions(b, Loc.GetString("cmd-weather-hint"));
    }
}
