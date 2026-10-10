using Content.Server.Chat.Systems;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._NC.Radio;
using Content.Shared.Radio;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Radio;

public sealed class IntegratedRadioSpeechSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    // Entities currently mid-broadcast, to stop a speaker's own radio message from
    // recursively re-triggering its broadcast. Tracked per-entity so one speaker never
    // suppresses another's legitimate broadcast.
    private readonly HashSet<EntityUid> _broadcasting = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioMicrophoneComponent, EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<RadioMicrophoneComponent, IntrinsicUIOpenAttemptEvent>(OnRadioUiOpen);
    }

    private void OnRadioUiOpen(EntityUid uid, RadioMicrophoneComponent mic, IntrinsicUIOpenAttemptEvent args)
    {
        if (args.Cancelled || !HandheldRadioUiKey.Key.Equals(args.Key) || !HasComp<BorgChassisComponent>(uid))
            return;

        var speakerEnabled = CompOrNull<RadioSpeakerComponent>(uid)?.Enabled ?? false;
        _ui.SetUiState(uid, HandheldRadioUiKey.Key,
            new HandheldRadioBoundUIState(mic.Enabled, speakerEnabled, mic.Frequency));
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
