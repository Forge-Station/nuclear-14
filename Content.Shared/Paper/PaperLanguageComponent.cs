// Forge-Change: paper writing in multiple languages
using Content.Shared.Language;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Paper;

/// <summary>
/// One stretch of handwriting in a single language.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class PaperLanguageSegment
{
    [DataField]
    public string Text = string.Empty;

    [DataField]
    public ProtoId<LanguagePrototype> Language = "English";
}

/// <summary>
/// Paper written in one or more languages. Unknown stretches are obfuscated for the reader.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PaperLanguageComponent : Component
{
    /// <summary>
    /// Language of the most recent stretch, and of pre-printed text until segments are recorded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<LanguagePrototype> Language = "English";

    [DataField, AutoNetworkedField]
    public List<PaperLanguageSegment> Segments = new();
}
