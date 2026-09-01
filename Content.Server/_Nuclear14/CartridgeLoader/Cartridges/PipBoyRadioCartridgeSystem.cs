using Content.Server.CartridgeLoader;
using Content.Shared._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.Audio.Jukebox;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Nuclear14.CartridgeLoader.Cartridges;

public sealed class PipBoyRadioCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoaderSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PipBoyRadioCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<PipBoyRadioCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<PipBoyRadioCartridgeComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnUiReady(
        EntityUid uid,
        PipBoyRadioCartridgeComponent component,
        CartridgeUiReadyEvent args)
    {
        component.LoaderUid = args.Loader;
        UpdateUiState(args.Loader, component);
    }

    private void OnUiMessage(
        EntityUid uid,
        PipBoyRadioCartridgeComponent component,
        CartridgeMessageEvent args)
    {
        if (args is not PipBoyRadioUiMessageEvent message)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        component.LoaderUid = loaderUid;
        component.ListenerUid = args.Actor;

        switch (message.Action)
        {
            case PipBoyRadioAction.Select:
                if (message.SongId is not { } songId)
                    break;

                if (!component.Songs.Contains(songId) ||
                    !_prototypeManager.HasIndex<JukeboxPrototype>(songId))
                    break;

                SelectSong(
                    component,
                    songId,
                    component.Playing);

                break;

            case PipBoyRadioAction.Play:
                Play(component);
                break;

            case PipBoyRadioAction.Pause:
                Pause(component);
                break;

            case PipBoyRadioAction.Stop:
                Stop(component);
                break;

            case PipBoyRadioAction.Previous:
                SelectRelative(
                    loaderUid,
                    component,
                    -1,
                    component.Playing);
                break;

            case PipBoyRadioAction.Next:
                SelectRelative(
                    loaderUid,
                    component,
                    1,
                    component.Playing);
                break;
        }

        UpdateUiState(loaderUid, component);
    }

    private void SelectSong(
        PipBoyRadioCartridgeComponent component,
        ProtoId<JukeboxPrototype> songId,
        bool startPlayback)
    {
        Stop(component);

        component.SelectedSongId = songId;

        if (startPlayback)
            Play(component);
    }

    private bool SelectRelative(
        EntityUid loaderUid,
        PipBoyRadioCartridgeComponent component,
        int direction,
        bool startPlayback)
    {
        var songs = component.Songs;

        if (songs.Count == 0)
            return false;

        var currentIndex = -1;

        if (component.SelectedSongId is { } currentSong)
            currentIndex = songs.FindIndex(song => song == currentSong);

        int nextIndex;

        if (currentIndex < 0)
        {
            nextIndex = direction >= 0
                ? 0
                : songs.Count - 1;
        }
        else
        {
            nextIndex =
                (currentIndex + direction + songs.Count)
                % songs.Count;
        }

        SelectSong(
            component,
            songs[nextIndex],
            startPlayback);

        return true;
    }

    private void Play(
        PipBoyRadioCartridgeComponent component)
    {
        if (component.WearerUid is not { } wearer ||
            component.ListenerUid != wearer)
            return;

        if (Exists(component.AudioStream))
        {
            _audio.SetState(
                component.AudioStream,
                AudioState.Playing);

            component.Playing = true;
            component.Paused = false;
            component.PlaybackGrace = 0.5f;
            return;
        }

        if (component.SelectedSongId is not { } songId ||
            !_prototypeManager.TryIndex(
                songId,
                out var jukeboxPrototype))
            return;

        if (component.ListenerUid is not { } listener ||
            !Exists(listener))
            return;

        component.AudioStream =
            _audio.PlayGlobal(
                jukeboxPrototype.Path,
                listener,
                AudioParams.Default
            )?.Entity;

        component.Playing =
            component.AudioStream != null;

        component.Paused = false;
        component.PlaybackGrace = 0.75f;
    }

    private void Pause(
        PipBoyRadioCartridgeComponent component)
    {
        if (!Exists(component.AudioStream))
            return;

        _audio.SetState(
            component.AudioStream,
            AudioState.Paused);

        component.Playing = false;
        component.Paused = true;
        component.PlaybackGrace = 0f;
    }

    private void Stop(
        PipBoyRadioCartridgeComponent component)
    {
        component.AudioStream =
            _audio.Stop(component.AudioStream);

        component.Playing = false;
        component.Paused = false;
        component.PlaybackGrace = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query =
            EntityQueryEnumerator<PipBoyRadioCartridgeComponent>();

        while (query.MoveNext(
                   out _,
                   out var component))
        {
            if (!component.Playing)
                continue;

            if (component.PlaybackGrace > 0f)
            {
                component.PlaybackGrace -= frameTime;
                continue;
            }

            if (_audio.IsPlaying(component.AudioStream))
                continue;

            // The track finished naturally.
            component.AudioStream =
                _audio.Stop(component.AudioStream);

            component.Playing = false;
            component.Paused = false;

            if (component.LoaderUid is not { } loaderUid ||
                !Exists(loaderUid))
                continue;

            // Automatically start the next track.
            SelectRelative(
                loaderUid,
                component,
                1,
                true);

            UpdateUiState(loaderUid, component);
        }
    }

    private void OnShutdown(
        EntityUid uid,
        PipBoyRadioCartridgeComponent component,
        ComponentShutdown args)
    {
        Stop(component);
    }

    private void UpdateUiState(
        EntityUid loaderUid,
        PipBoyRadioCartridgeComponent component)
    {
        var state = new PipBoyRadioUiState(
            component.Songs,
            component.SelectedSongId,
            component.Playing,
            component.Paused);

        _cartridgeLoaderSystem.UpdateCartridgeUiState(
            loaderUid,
            state);
    }
}
