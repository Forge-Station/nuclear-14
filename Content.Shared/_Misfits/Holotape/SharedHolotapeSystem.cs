using System;
using System.Collections.Generic;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

// #Misfits Add - Shared UI contract for the holotape/terminal viewer system.
// Defines the UI key, BUI state, BUI messages, and serializable types needed by both server and client.

namespace Content.Shared._Misfits.Holotape;

/// <summary>
/// UI key for the green-on-black holotape/terminal viewer interface.
/// Used by both holotapes (via reader interaction) and terminals (via direct click).
/// </summary>
[Serializable, NetSerializable]
public enum HolotapeUiKey : byte
{
    Key
}

/// <summary>
/// State sent from server to client containing the title and body text to display,
/// plus optional notebook notes for terminals with TerminalNotebookComponent.
/// </summary>
[Serializable, NetSerializable]
public sealed class HolotapeBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// Display title shown at the top of the terminal viewer (e.g. "OVERSEER'S LOG #3").
    /// </summary>
    public readonly string Title;

    /// <summary>
    /// Body content in BB code format, rendered in green monospace text.
    /// </summary>
    public readonly string Content;

    // #Misfits Add - Notes list and viewer identity for the notebook tab
    /// <summary>
    /// Notes stored on this terminal. Null if this terminal has no notebook component.
    /// </summary>
    public readonly List<TerminalNoteEntry>? Notes;

    /// <summary>
    /// The NetUserId of the player currently viewing the UI.
    /// Used to determine which notes the viewer may delete (only their own).
    /// </summary>
    public readonly NetUserId? ViewerUserId;

    // #Misfits Add - True when viewing a holotape item (inserted in reader/Pip-Boy),
    // False when viewing a built-in terminal. Controls header and window title text.
    public readonly bool IsHolotapeItem;

    public HolotapeBoundUserInterfaceState(
        string title,
        string content,
        List<TerminalNoteEntry>? notes = null,
        NetUserId? viewerUserId = null,
        bool isHolotapeItem = false)
    {
        Title = title;
        Content = content;
        Notes = notes;
        ViewerUserId = viewerUserId;
        IsHolotapeItem = isHolotapeItem;
    }
}

// ── BUI Messages (client → server) ─────────────────────────────────────────

// #Misfits Add - Messages for the terminal notes tab

/// <summary>
/// Client requests the current note list for a terminal (e.g. when switching to Notes tab).
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestTerminalNotesMessage : BoundUserInterfaceMessage;

/// <summary>
/// Client submits a new note to be stored on this terminal.
/// </summary>
[Serializable, NetSerializable]
public sealed class SubmitTerminalNoteMessage : BoundUserInterfaceMessage
{
    public readonly string AuthorName;
    public readonly string Text;

    public SubmitTerminalNoteMessage(string authorName, string text)
    {
        AuthorName = authorName;
        Text = text;
    }
}

/// <summary>
/// Client requests deletion of a note by its Guid.
/// Server verifies the requesting player owns the note before removing it.
/// </summary>
[Serializable, NetSerializable]
public sealed class DeleteTerminalNoteMessage : BoundUserInterfaceMessage
{
    public readonly Guid NoteId;

    public DeleteTerminalNoteMessage(Guid noteId)
    {
        NoteId = noteId;
    }
}
