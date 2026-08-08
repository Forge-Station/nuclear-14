using Robust.Shared.GameObjects;

// #Misfits Add - Picks random terminal content from a dataset pool on MapInit.
// Works with HolotapeDataComponent to populate title/content fields.

namespace Content.Shared._Misfits.Holotape;

/// <summary>
/// On MapInit, picks a random entry from ContentPool datasets
/// and writes it into the entity's HolotapeDataComponent.
/// Each dataset entry is an FTL key prefix; the system appends "-title" and "-content" suffixes.
/// </summary>
[RegisterComponent]
public sealed partial class TerminalRandomContentComponent : Component
{
    /// <summary>
    /// List of dataset prototype IDs to randomly pick from.
    /// Each value in the dataset should be an FTL key prefix.
    /// </summary>
    [DataField]
    public List<string> ContentPool = new();
}
