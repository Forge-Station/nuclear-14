using Content.Shared._Forge.QuestInstance;
using Content.Shared.Weather;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client.Weather;

public sealed class WeatherAudioCleanupSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeatherComponent, ComponentShutdown>(OnWeatherShutdown);
        SubscribeNetworkEvent<QuestInstanceWeatherAudioCleanupEvent>(OnQuestInstanceWeatherCleanup);
    }

    private void OnQuestInstanceWeatherCleanup(QuestInstanceWeatherAudioCleanupEvent ev)
    {
        var keepMapUid = ResolveKeepMapUid(ev.KeepMapUid);
        CleanupWeatherAudio(keepMapUid);
    }

    private EntityUid? ResolveKeepMapUid(NetEntity? keepMapNetUid)
    {
        if (keepMapNetUid is { } keepNet && TryGetEntity(keepNet, out var keepMapUid) && !Deleted(keepMapUid))
            return keepMapUid;

        if (_player.LocalEntity is { } playerUid && !Deleted(playerUid))
            return Transform(playerUid).MapUid;

        return null;
    }

    private void OnWeatherShutdown(EntityUid uid, WeatherComponent component, ComponentShutdown args)
    {
        StopAllStreams(component);
    }

    private void CleanupWeatherAudio(EntityUid? keepMapUid)
    {
        var query = EntityQueryEnumerator<WeatherComponent, TransformComponent>();
        while (query.MoveNext(out _, out var weather, out var xform))
        {
            if (!HasActiveStreams(weather))
                continue;

            if (keepMapUid != null && xform.MapUid == keepMapUid)
                continue;

            StopAllStreams(weather);
        }
    }

    private static bool HasActiveStreams(WeatherComponent component)
    {
        foreach (var data in component.Weather.Values)
        {
            if (data.Stream != null)
                return true;
        }

        return false;
    }

    private void StopAllStreams(WeatherComponent component)
    {
        foreach (var data in component.Weather.Values)
        {
            if (data.Stream is not { } streamUid)
                continue;

            if (TryComp(streamUid, out AudioComponent? audioComp))
                _audio.SetState(streamUid, AudioState.Stopped, true, audioComp);

            _audio.Stop(streamUid);
            data.Stream = null;
        }
    }
}
