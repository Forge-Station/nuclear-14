using Content.Shared.Weather;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;

namespace Content.Client.Weather;

public sealed class WeatherAudioCleanupSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly HashSet<string> _weatherAudioFiles = new();
    private EntityUid? _localEntity;
    private EntityUid? _localMap;

    public override void Initialize()
    {
        base.Initialize();

        BuildWeatherAudioFileCache();

        SubscribeLocalEvent<WeatherComponent, ComponentShutdown>(OnWeatherShutdown);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
        SubscribeLocalEvent<EntParentChangedMessage>(OnParentChanged);

        _localEntity = _player.LocalEntity;
        _localMap = _localEntity is { } uid ? Transform(uid).MapUid : null;
        CleanupWeatherAudioForCurrentMap(_localMap);
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent args)
    {
        _localEntity = args.Entity;
        _localMap = Transform(args.Entity).MapUid;
        CleanupWeatherAudioForCurrentMap(_localMap);
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _localEntity = null;
        _localMap = null;
        CleanupWeatherAudioForCurrentMap(null);
    }

    private void OnParentChanged(ref EntParentChangedMessage args)
    {
        if (_localEntity is not { } local || args.Entity != local)
            return;

        if (args.Transform.MapUid == _localMap)
            return;

        _localMap = args.Transform.MapUid;
        CleanupWeatherAudioForCurrentMap(_localMap);
    }

    private void OnWeatherShutdown(EntityUid uid, WeatherComponent component, ComponentShutdown args)
    {
        StopAllStreams(component);
        StopStrayWeatherAudioEntities();
    }

    private void CleanupWeatherAudioForCurrentMap(EntityUid? currentMap)
    {
        var query = EntityQueryEnumerator<WeatherComponent, TransformComponent>();
        while (query.MoveNext(out _, out var weather, out var xform))
        {
            if (currentMap != null && xform.MapUid == currentMap)
                continue;

            StopAllStreams(weather);
        }

        StopStrayWeatherAudioEntities();
    }

    private void StopAllStreams(WeatherComponent component)
    {
        foreach (var data in component.Weather.Values)
        {
            if (data.Stream is not { } streamUid)
                continue;

            if (TryComp(streamUid, out AudioComponent? streamAudio))
                _audio.SetState(streamUid, AudioState.Stopped, true, streamAudio);

            data.Stream = _audio.Stop(streamUid);
        }
    }

    private void StopStrayWeatherAudioEntities()
    {
        var referencedStreams = new HashSet<EntityUid>();

        var weatherQuery = EntityQueryEnumerator<WeatherComponent>();
        while (weatherQuery.MoveNext(out _, out var weather))
            foreach (var data in weather.Weather.Values)
                if (data.Stream is { } streamUid)
                    referencedStreams.Add(streamUid);

        var audioQuery = EntityQueryEnumerator<AudioComponent>();
        while (audioQuery.MoveNext(out var uid, out var audio))
        {
            if (!_weatherAudioFiles.Contains(NormalizeAudioPath(audio.FileName)))
                continue;

            if (referencedStreams.Contains(uid))
                continue;

            _audio.SetState(uid, AudioState.Stopped, true, audio);
            _audio.Stop(uid, audio);
        }
    }

    private void BuildWeatherAudioFileCache()
    {
        _weatherAudioFiles.Clear();

        foreach (var weather in _proto.EnumeratePrototypes<WeatherPrototype>())
        {
            if (weather.Sound is null)
                continue;

            switch (weather.Sound)
            {
                case SoundPathSpecifier path when path.Path != default:
                    _weatherAudioFiles.Add(NormalizeAudioPath(path.Path.ToString()));
                    break;

                case SoundCollectionSpecifier collection when collection.Collection is { } collectionId
                    && _proto.TryIndex(collectionId, out SoundCollectionPrototype? soundCollection):
                    foreach (var file in soundCollection.PickFiles)
                        _weatherAudioFiles.Add(NormalizeAudioPath(file.ToString()));
                    break;
            }
        }
    }

    private static string NormalizeAudioPath(string path)
    {
        return path.ToLowerInvariant();
    }
}
