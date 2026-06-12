using Content.Shared.DoAfter;
using Content.Shared.Silicons.Laws;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Silicons;

/// <summary>
/// Forge-Change: a "law punch card". Using it in hand opens an editable law window (reusing the
/// silicon law editor) so the player can write/add/remove laws. Using it on a silicon with a law
/// provider, while its maintenance panel is open, uploads the card's laws. Lets players program a junkbot.
/// </summary>
[RegisterComponent]
public sealed partial class LawCardComponent : Component
{
    /// <summary>
    /// The laws stored on this card. Edited via the card's UI, applied to a silicon on upload.
    /// Defaults come from the prototype (a starting template).
    /// </summary>
    [DataField]
    public List<SiliconLaw> Laws = new();

    /// <summary>
    /// How long the upload do-after takes, in seconds.
    /// </summary>
    [DataField]
    public float UploadDelay = 5f;
}

/// <summary>
/// Raised on the card when the upload do-after finishes. Target is the silicon being programmed.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class LawCardDoAfterEvent : SimpleDoAfterEvent
{
}
