using Content.Server.Chat.Systems;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Radio;

// Моб, который сам себе микрофон и динамик (RadioMicrophone+RadioSpeaker на самой сущности), не может вещать ванильным путём:
// RadioDeviceSystem.OnListen роняет речь источника с RadioSpeaker (анти-петля динамик→микрофон). Ловим речь напрямую через
// EntitySpokeEvent (на ListenEvent уже подписан RadioDeviceSystem) и сами шлём её в эфир на канал/частоту микрофона.
public sealed class IntegratedRadioSpeechSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    // Рвёт петлю само-эха: динамик синхронно проговаривает принятое (EntitySpokeEvent) внутри нашего же SendRadioMessage.
    private bool _broadcasting;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioMicrophoneComponent, EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntityUid uid, RadioMicrophoneComponent mic, EntitySpokeEvent args)
    {
        // открытый микрофон: только при включённом микрофоне и только обычная речь (радио-префиксы шлёт IntrinsicRadioTransmitter).
        if (_broadcasting || !mic.Enabled || args.Channel != null)
            return;

        _broadcasting = true;
        try
        {
            _radio.SendRadioMessage(uid, args.Message, _proto.Index<RadioChannelPrototype>(mic.BroadcastChannel), uid, frequency: mic.Frequency);
        }
        finally
        {
            _broadcasting = false;
        }
    }
}
