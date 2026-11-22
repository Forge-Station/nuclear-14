using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Speech;

/// <summary>
///     Modifies how far this entity's local speech travels, or forces it to whisper.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SpeechModifierComponent : Component
{
    /// <summary>
    ///     Multiplier applied to the entity's local voice range.
    /// </summary>
    [DataField]
    public float VoiceRangeMultiplier = 1f;

    /// <summary>
    ///     If true, everything the entity says is sent as a whisper.
    /// </summary>
    [DataField]
    public bool WhisperOnly;
}
