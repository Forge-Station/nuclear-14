using Content.Shared.Humanoid;

namespace Content.Shared._NC.TTS;

/// <summary>
/// Corvax-TTS: default TTS voices.
/// Previously lived on SharedHumanoidAppearanceSystem, which upstream removed.
/// </summary>
public static class TtsDefaults
{
    public const string DefaultVoice = "Papich";

    public static readonly Dictionary<Sex, string> DefaultSexVoice = new()
    {
        { Sex.Male, "Papich" },
        { Sex.Female, "Charlotte" },
        { Sex.Unsexed, "MisterGutsyFl3" },
    };
}
