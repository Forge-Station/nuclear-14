using Content.Server.Chat.Systems;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Radio;

// Если существо само себе и микрофон, и динамик (рация встроена прямо в него), обычная рация его собственную речь
// в эфир не пускает — там стоит защита, чтобы динамик не зациклился на свой же микрофон. Поэтому ловим речь существа
// напрямую и отправляем её в эфир сами, на тот канал и частоту, что выставлены у микрофона.
public sealed class IntegratedRadioSpeechSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    // флаг, чтобы рация не пошла по кругу: динамик повторяет принятое сообщение вслух, и без него оно снова ушло бы в эфир.
    private bool _broadcasting;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioMicrophoneComponent, EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntityUid uid, RadioMicrophoneComponent mic, EntitySpokeEvent args)
    {
        // только если микрофон включён и это обычная речь, а не радио-команда (команды уходят своим путём).
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
