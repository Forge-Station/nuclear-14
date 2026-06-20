using Content.Server.Chat.Systems;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Radio;

public sealed class IntegratedRadioSpeechSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private bool _broadcasting;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioMicrophoneComponent, EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntityUid uid, RadioMicrophoneComponent mic, EntitySpokeEvent args)
    {
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
