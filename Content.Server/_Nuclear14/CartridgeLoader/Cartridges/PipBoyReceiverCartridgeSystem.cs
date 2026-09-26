using Content.Server.CartridgeLoader;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;

namespace Content.Server._Nuclear14.CartridgeLoader.Cartridges;

public sealed class PipBoyReceiverCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _loader = default!;
    [Dependency] private readonly RadioDeviceSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PipBoyReceiverCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<PipBoyReceiverCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    private void OnUiReady(EntityUid uid, PipBoyReceiverCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUi(args.Loader, component);
    }

    private void OnUiMessage(EntityUid uid, PipBoyReceiverCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not PipBoyReceiverUiMessageEvent message)
            return;

        var receiver = GetEntity(args.LoaderUid);
        if (!HasComp<RadioSpeakerComponent>(receiver) || !HasComp<RadioMicrophoneComponent>(receiver))
            return;

        if (message.Channel is { } channel && component.Channels.Contains(channel))
            _radio.SetReceiverChannel(receiver, channel);

        if (message.Enabled is { } enabled)
            _radio.SetSpeakerEnabled(receiver, args.Actor, enabled, quiet: true);

        UpdateUi(receiver, component);
    }

    private void UpdateUi(EntityUid receiver, PipBoyReceiverCartridgeComponent component)
    {
        if (!TryComp<RadioSpeakerComponent>(receiver, out var speaker) ||
            !TryComp<RadioMicrophoneComponent>(receiver, out var microphone))
            return;

        _loader.UpdateCartridgeUiState(receiver,
            new PipBoyReceiverUiState(component.Channels, microphone.BroadcastChannel, speaker.Enabled));
    }
}
