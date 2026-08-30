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

    // Entities currently mid-broadcast, to stop a speaker's own radio message from
    // recursively re-triggering its broadcast. Tracked per-entity so one speaker never
    // suppresses another's legitimate broadcast.
    private readonly HashSet<EntityUid> _broadcasting = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioMicrophoneComponent, EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntityUid uid, RadioMicrophoneComponent mic, EntitySpokeEvent args)
    {
        if (!mic.Enabled || args.Channel != null || !_broadcasting.Add(uid))
            return;

        try
        {
            _radio.SendRadioMessage(uid, args.Message, _proto.Index<RadioChannelPrototype>(mic.BroadcastChannel), uid, frequency: mic.Frequency);
        }
        finally
        {
            _broadcasting.Remove(uid);
        }
    }
}
