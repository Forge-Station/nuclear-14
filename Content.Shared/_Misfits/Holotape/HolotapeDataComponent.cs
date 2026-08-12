using Robust.Shared.GameObjects;

// #Misfits Add - Stores readable content for holotapes and terminals.
// Title and Content are FTL locale keys resolved on MapInit.

namespace Content.Shared._Misfits.Holotape;

/// <summary>
/// Holds the title and body text for a holotape or terminal entry.
/// Both fields are treated as FTL locale keys and resolved on MapInit.
/// </summary>
[RegisterComponent]
public sealed partial class HolotapeDataComponent : Component
{
    /// <summary>
    /// FTL key for the display title. Resolved to localized string on MapInit.
    /// </summary>
    [DataField]
    public string Title = string.Empty;

    /// <summary>
    /// FTL key for the body content (BB code). Resolved to localized string on MapInit.
    /// </summary>
    [DataField]
    public string Content = string.Empty;

    /// <summary>
    /// Tracks whether locale resolution has already been applied.
    /// Prevents double-localization if MapInit fires more than once.
    /// </summary>
    [DataField]
    public bool Localized;
}
