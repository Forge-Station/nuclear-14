using Content.Server._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.Inventory.Events;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Nuclear14.CartridgeLoader;

public sealed class PipBoyRadioWearerSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PipBoyRadioWearerComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnUnequipped(
        EntityUid uid,
        PipBoyRadioWearerComponent component,
        GotUnequippedEvent args)
    {

        var query = EntityQueryEnumerator<PipBoyRadioCartridgeComponent>();

        while (query.MoveNext(out var cartridgeUid, out var radio))
        {
            if (radio.LoaderUid != uid)
                continue;

            if (radio.AudioStream is { } stream && Exists(stream))
                _audio.Stop(stream);

            radio.AudioStream = null;
            radio.Playing = false;
            radio.Paused = false;
            radio.ListenerUid = null;
        }
    }
}
