using System;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

// #Misfits Add - Data model for a single player-written terminal note.
// Serializable over the network and to JSON on disk.

namespace Content.Shared._Misfits.Holotape;

/// <summary>
/// A single note entry stored in a terminal's notebook.
/// Sent over the network as part of BUI state.
/// </summary>
[Serializable, NetSerializable]
public sealed class TerminalNoteEntry
{
    /// <summary>
    /// Unique identifier for this note entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name chosen by the author. May be "Anonymous".
    /// </summary>
    public string AuthorName { get; set; } = "Anonymous";

    /// <summary>
    /// The text body of the note.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// When this note was written (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The NetUserId of the player who wrote this note.
    /// Used to determine deletion rights — only the author can delete.
    /// </summary>
    public NetUserId? AuthorUserId { get; set; }
}
