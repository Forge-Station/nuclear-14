using Content.Shared._NC.TTS;
using Content.Shared.Inventory;
using Content.Shared.VoiceMask;
using Robust.Shared.Prototypes;

namespace Content.Server.VoiceMask;

public partial class VoiceMaskSystem
{
    // Corvax-TTS: upstream's VoiceMaskSystem no longer injects IPrototypeManager, so declare it here.
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private void InitializeTTS()
    {
        SubscribeLocalEvent<VoiceMaskComponent, InventoryRelayedEvent<TransformSpeakerVoiceEvent>>(OnSpeakerVoiceTransform);
        SubscribeLocalEvent<VoiceMaskComponent, VoiceMaskChangeVoiceMessage>(OnChangeVoice);
    }

    private void OnSpeakerVoiceTransform(EntityUid uid, VoiceMaskComponent component, InventoryRelayedEvent<TransformSpeakerVoiceEvent> args)
    {
        args.Args.VoiceId = component.VoiceId;
    }

    private void OnChangeVoice(Entity<VoiceMaskComponent> entity, ref VoiceMaskChangeVoiceMessage msg)
    {
        if (msg.Voice is { } id && !_proto.HasIndex<TTSVoicePrototype>(id))
            return;

        entity.Comp.VoiceId = msg.Voice;

        _popupSystem.PopupEntity(Loc.GetString("voice-mask-voice-popup-success"), entity);

        UpdateUI(entity);
    }
}
