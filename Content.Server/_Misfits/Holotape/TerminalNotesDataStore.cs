using System;
using System.Collections.Generic;
using System.Text.Json;
using Content.Shared._Misfits.Holotape;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Utility;

// #Misfits Add - Persistent data store for terminal notes.
// Saves/loads terminal notes to/from JSON at data/terminal_notes.json.

namespace Content.Server._Misfits.Holotape;

/// <summary>
/// Singleton IoC service that persists terminal notes to disk across round restarts.
/// Data is stored at data/terminal_notes.json via IWritableDirProvider (UserData).
/// </summary>
public sealed class TerminalNotesDataStore
{
    [Dependency] private readonly IResourceManager _resourceManager = default!;

    private static readonly ResPath StoragePath = new("/terminal_notes.json");

    // #Misfits Fix - Defer sawmill init to Initialize(); field initializers run before IoC is populated
    private ISawmill _sawmill = default!;

    /// <summary>
    /// In-memory store: terminalId → list of notes.
    /// </summary>
    private Dictionary<string, List<TerminalNoteEntryDto>> _store = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads existing notes from disk. Call once at system init.
    /// </summary>
    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("terminal.notes"); // safe here — IoC is fully built
        Load();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a copy of the note list for the given terminal. Never null.
    /// </summary>
    public List<TerminalNoteEntry> GetNotes(string terminalId)
    {
        if (_store.TryGetValue(terminalId, out var dtos))
        {
            var result = new List<TerminalNoteEntry>(dtos.Count);
            foreach (var dto in dtos)
                result.Add(dto.ToEntry());
            return result;
        }
        return new List<TerminalNoteEntry>();
    }

    /// <summary>
    /// Appends a note to the given terminal and persists immediately.
    /// </summary>
    public void AddNote(string terminalId, TerminalNoteEntry entry)
    {
        if (!_store.TryGetValue(terminalId, out var notes))
        {
            notes = new List<TerminalNoteEntryDto>();
            _store[terminalId] = notes;
        }
        notes.Add(TerminalNoteEntryDto.FromEntry(entry));
        Save();
    }

    /// <summary>
    /// Removes a note by id from the given terminal and persists.
    /// Returns true if a note was actually removed.
    /// </summary>
    public bool RemoveNote(string terminalId, Guid noteId)
    {
        if (!_store.TryGetValue(terminalId, out var notes))
            return false;

        var removed = notes.RemoveAll(n => n.Id == noteId) > 0;
        if (removed)
            Save();
        return removed;
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes the in-memory store to JSON on disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_store, JsonOptions);
            _resourceManager.UserData.WriteAllText(StoragePath, json);
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to save terminal notes: {ex}");
        }
    }

    private void Load()
    {
        try
        {
            if (!_resourceManager.UserData.Exists(StoragePath))
            {
                _sawmill.Info("No terminal_notes.json found; starting fresh.");
                return;
            }

            var json = _resourceManager.UserData.ReadAllText(StoragePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<TerminalNoteEntryDto>>>(json, JsonOptions);
            if (loaded != null)
            {
                _store = loaded;
                _sawmill.Info($"Loaded terminal notes for {_store.Count} terminal(s).");
            }
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to load terminal notes: {ex}");
        }
    }

    // ── DTO ──────────────────────────────────────────────────────────────────
    // System.Text.Json-friendly DTO (NetUserId isn't directly serializable by STJ)

    /// <summary>
    /// JSON-friendly data transfer object for terminal notes.
    /// NetUserId is stored as its underlying Guid string.
    /// </summary>
    private sealed class TerminalNoteEntryDto
    {
        public Guid Id { get; set; }
        public string AuthorName { get; set; } = "Anonymous";
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Guid? AuthorUserIdGuid { get; set; }

        public static TerminalNoteEntryDto FromEntry(TerminalNoteEntry e) => new()
        {
            Id = e.Id,
            AuthorName = e.AuthorName,
            Text = e.Text,
            Timestamp = e.Timestamp,
            AuthorUserIdGuid = e.AuthorUserId?.UserId,
        };

        public TerminalNoteEntry ToEntry() => new()
        {
            Id = Id,
            AuthorName = AuthorName,
            Text = Text,
            Timestamp = Timestamp,
            AuthorUserId = AuthorUserIdGuid.HasValue
                ? new Robust.Shared.Network.NetUserId(AuthorUserIdGuid.Value)
                : null,
        };
    }
}
