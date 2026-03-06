using Content.Shared._Forge.QuestInstance;
using Content.Shared.Weather;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client.Weather;

public sealed class WeatherAudioCleanupSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private EntityUid? _currentMap;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeatherComponent, ComponentShutdown>(OnWeatherShutdown);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
        SubscribeLocalEvent<WeatherAudioListenerComponent, EntParentChangedMessage>(OnPlayerParentChanged);
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent args)
    {
        EnsureComp<WeatherAudioListenerComponent>(args.Entity);

        _currentMap = Transform(args.Entity).MapUid;
        CleanupWeatherAudioForCurrentMap(_currentMap);
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        RemComp<WeatherAudioListenerComponent>(args.Entity);

        _currentMap = null;
        CleanupWeatherAudioForCurrentMap(null);
    }

    private void OnPlayerParentChanged(EntityUid uid, WeatherAudioListenerComponent comp, ref EntParentChangedMessage args)
    {
        var newMap = args.Transform.MapUid;
        if (newMap == _currentMap)
            return;

        _currentMap = newMap;
        CleanupWeatherAudioForCurrentMap(_currentMap);
    }

    private void OnWeatherShutdown(EntityUid uid, WeatherComponent component, ComponentShutdown args)
    {
        StopAllStreams(component);
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
    }

    private void StopAllStreams(WeatherComponent component)
    {
        foreach (var data in component.Weather.Values)
        {
            if (data.Stream is not { } streamUid)
                continue;

            _audio.Stop(streamUid);
            data.Stream = null;
        }
    }
}
