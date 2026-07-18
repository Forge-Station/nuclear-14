using Robust.Shared.GameObjects;

// #Misfits Add - Marks a terminal as having a persistent notes notebook.
// Notes are keyed by TerminalId and persist across round restarts.

namespace Content.Shared._Misfits.Holotape;

/// <summary>
/// Marks a terminal entity as having a persistent notes notebook.
/// The TerminalId is the key for note storage on disk.
/// </summary>
[RegisterComponent]
public sealed partial class TerminalNotebookComponent : Component
{
    /// <summary>
    /// Unique identifier for this terminal's note storage.
    /// Set in YAML to a stable, unique string per terminal map placement.
    /// </summary>
    [DataField("terminalId")]
    public string TerminalId { get; set; } = string.Empty;
}
