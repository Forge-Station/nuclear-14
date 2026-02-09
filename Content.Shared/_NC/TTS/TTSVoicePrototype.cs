using Content.Shared._NC.Sponsor;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;


namespace Content.Shared._NC.TTS;

/// <summary>
/// Prototype represent available TTS voices
/// </summary>
[Prototype("ttsVoice")]
// ReSharper disable once InconsistentNaming
public sealed partial class TTSVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField("name")]
    public string Name { get; set; } = string.Empty;

    [DataField("sex", required: true)]
    public Sex Sex { get; set; } = default!;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("speaker", required: true)]
    public string Speaker { get; set; } = string.Empty;

    /// <summary>
    /// Whether the species is available "at round start" (In the character editor)
    /// </summary>
    [DataField("roundStart")]
    public bool RoundStart { get; set; } = true;

    [DataField("sponsorLevel")]
    public SponsorLevel SponsorLevel = SponsorLevel.None;
}
