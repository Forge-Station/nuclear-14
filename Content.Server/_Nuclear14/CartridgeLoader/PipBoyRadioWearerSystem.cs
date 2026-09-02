using Content.Server._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;
using Content.Shared.Inventory.Events;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Nuclear14.CartridgeLoader;

public sealed class PipBoyRadioWearerSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PipBoyRadioWearerComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<PipBoyRadioWearerComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(
        EntityUid uid,
        PipBoyRadioWearerComponent component,
        GotEquippedEvent args)
    {
        var query = EntityQueryEnumerator<PipBoyRadioCartridgeComponent, CartridgeComponent>();

        while (query.MoveNext(out _, out var radio, out var cartridge))
        {
            if (cartridge.LoaderUid != uid)
                continue;

            radio.WearerUid = args.Equipee;
        }
    }

    private void OnUnequipped(
        EntityUid uid,
        PipBoyRadioWearerComponent component,
        GotUnequippedEvent args)
    {

        var query = EntityQueryEnumerator<PipBoyRadioCartridgeComponent, CartridgeComponent>();

        while (query.MoveNext(out _, out var radio, out var cartridge))
        {
            if (cartridge.LoaderUid != uid)
                continue;

            if (radio.AudioStream is { } stream && Exists(stream))
                _audio.Stop(stream);

            radio.AudioStream = null;
            radio.Playing = false;
            radio.Paused = false;
            radio.PlaybackGrace = 0f;
            radio.ListenerUid = null;
            radio.WearerUid = null;
        }
    }
}
