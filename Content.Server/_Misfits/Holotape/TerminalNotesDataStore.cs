using System;
using System.Collections.Generic;
using Content.Shared._Misfits.Holotape;
using Robust.Shared.Log;

// #Misfits Add - In-memory store for terminal notes.
// Forge-Change: notes are round-scoped and must not persist to disk or across restarts.

namespace Content.Server._Misfits.Holotape;

/// <summary>
/// Singleton IoC service that holds terminal notes in memory for the current round.
/// Cleared on round restart so notes do not carry over.
/// </summary>
public sealed class TerminalNotesDataStore
{
    // Forge-Change: Defer sawmill init to Initialize(); field initializers run before IoC is populated
    private ISawmill _sawmill = default!;

    /// <summary>
    /// In-memory store: terminalId → list of notes.
    /// </summary>
    private readonly Dictionary<string, List<TerminalNoteEntry>> _store = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Prepares the store for this process. Call once at system init.
    /// </summary>
    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("terminal.notes");
    }

    /// <summary>
    /// Drops every stored note. Called on round restart.
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        _sawmill.Debug("Cleared all terminal notes.");
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a copy of the note list for the given terminal. Never null.
    /// </summary>
    public List<TerminalNoteEntry> GetNotes(string terminalId)
    {
        if (_store.TryGetValue(terminalId, out var notes))
            return new List<TerminalNoteEntry>(notes);

        return new List<TerminalNoteEntry>();
    }

    /// <summary>
    /// Appends a note to the given terminal.
    /// </summary>
    public void AddNote(string terminalId, TerminalNoteEntry entry)
    {
        if (!_store.TryGetValue(terminalId, out var notes))
        {
            notes = new List<TerminalNoteEntry>();
            _store[terminalId] = notes;
        }

        notes.Add(entry);
        _sawmill.Debug($"Added note to terminal '{terminalId}', total={notes.Count}.");
    }

    /// <summary>
    /// Removes a note by id from the given terminal.
    /// Returns true if a note was actually removed.
    /// </summary>
    public bool RemoveNote(string terminalId, Guid noteId)
    {
        if (!_store.TryGetValue(terminalId, out var notes))
            return false;

        var removed = notes.RemoveAll(n => n.Id == noteId) > 0;
        if (removed)
            _sawmill.Debug($"Removed note {noteId} from terminal '{terminalId}'.");

        return removed;
    }
}
