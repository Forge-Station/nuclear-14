using Robust.Shared.GameObjects;

// #Misfits Add - Marks a terminal as having a notes notebook.
// Forge-Change: notes are keyed by TerminalId and last for the current round only.

namespace Content.Shared._Misfits.Holotape;

/// <summary>
/// Marks a terminal entity as having a notes notebook.
/// The TerminalId is the in-memory key for this round's notes.
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
