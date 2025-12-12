using Robust.Shared.Serialization;


namespace Content.Shared._NC.TTS;

[Serializable, NetSerializable]
public sealed class VoiceMaskChangeVoiceMessage : BoundUserInterfaceMessage
{
    public string Voice;

    public VoiceMaskChangeVoiceMessage(string voice)
    {
        Voice = voice;
    }
}
