using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Speech.Synthesis;

/// <summary>
/// A prototype for the available barges.
/// </summary>
[Prototype("bark")]
public sealed partial class BarkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    /// <summary>
    /// The name of the voice.
    /// </summary>
    [DataField("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A set of sounds used for speech.
    /// </summary>
    [DataField("soundFiles", required: true)]
    public List<string> SoundFiles { get; set; } = new();

    /// <summary>
    /// Whether it is available for selection.
    /// </summary>
    [DataField("roundStart")]
    public bool RoundStart { get; set; } = true;
}
