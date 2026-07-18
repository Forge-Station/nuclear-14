using System;
using Content.Server.UserInterface;
using Content.Shared._Misfits.Holotape;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

// #Misfits Add - Server system for terminal notebook note CRUD.
// Handles player-submitted notes, deletion (own notes only), and state push.
// Auto-generates deterministic terminal IDs from grid position on MapInit.

namespace Content.Server._Misfits.Holotape;

/// <summary>
/// Handles BUI messages for terminals with TerminalNotebookComponent.
/// Reads/writes notes via TerminalNotesDataStore and pushes updated state.
/// </summary>
public sealed class TerminalNotebookSystem : EntitySystem
{
    [Dependency] private readonly TerminalNotesDataStore _dataStore = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private readonly ISawmill _sawmill = Logger.GetSawmill("terminal.notebook");

    public override void Initialize()
    {
        base.Initialize();

        // Initialize the persistent data store (loads from disk)
        _dataStore.Initialize();

        // Auto-generate a deterministic terminalId from grid position on map spawn
        SubscribeLocalEvent<TerminalNotebookComponent, MapInitEvent>(OnMapInit);

        // Subscribe to client BUI messages on entities with TerminalNotebookComponent
        SubscribeLocalEvent<TerminalNotebookComponent, RequestTerminalNotesMessage>(OnRequestNotes);
        SubscribeLocalEvent<TerminalNotebookComponent, SubmitTerminalNoteMessage>(OnSubmitNote);
        SubscribeLocalEvent<TerminalNotebookComponent, DeleteTerminalNoteMessage>(OnDeleteNote);
    }

    // ── MapInit: auto-generate terminal ID from position ─────────────────────

    /// <summary>
    /// On MapInit, if the terminal still has the default ID, generate a unique
    /// deterministic ID based on its world grid position. This ensures the same
    /// physical terminal gets the same note storage across round restarts.
    /// </summary>
    private void OnMapInit(EntityUid uid, TerminalNotebookComponent notebook, MapInitEvent args)
    {
        if (!string.IsNullOrEmpty(notebook.TerminalId) && notebook.TerminalId != "default-terminal")
            return;

        // Build a deterministic key from the entity's world position
        var xform = Transform(uid);
        var pos = xform.WorldPosition;
        var mapId = xform.MapID;

        // Prototype ID for extra uniqueness (in case two different terminal types overlap)
        var protoId = MetaData(uid).EntityPrototype?.ID ?? "unknown";

        // Round coordinates to 1 decimal to handle floating-point jitter
        notebook.TerminalId = $"{protoId}_{mapId}_{pos.X:F1}_{pos.Y:F1}";
        _sawmill.Debug($"Auto-assigned terminalId '{notebook.TerminalId}' to entity {uid}.");
    }

    // ── Message Handlers ─────────────────────────────────────────────────────

    /// <summary>
    /// Client switched to the Notes tab — push the current notes to them.
    /// </summary>
    private void OnRequestNotes(EntityUid uid, TerminalNotebookComponent notebook, RequestTerminalNotesMessage msg)
    {
        PushNotesState(uid, notebook, msg.Actor);
    }

    /// <summary>
    /// Client submitted a new note entry.
    /// </summary>
    private void OnSubmitNote(EntityUid uid, TerminalNotebookComponent notebook, SubmitTerminalNoteMessage msg)
    {
        // Reject empty notes
        if (string.IsNullOrWhiteSpace(msg.Text))
            return;

        var userId = GetUserId(msg.Actor);

        // #Misfits Tweak - Use the player's actual in-game character name instead of a typed author field
        var authorName = MetaData(msg.Actor).EntityName;
        if (string.IsNullOrWhiteSpace(authorName))
            authorName = "Unknown";  // fallback if entity has no name

        // Sanitise note text (cap at 2000 chars to prevent abuse)
        var text = msg.Text.Trim();
        if (text.Length > 2000)
            text = text[..2000];

        var entry = new TerminalNoteEntry
        {
            Id = Guid.NewGuid(),
            AuthorName = authorName,
            Text = text,
            Timestamp = DateTime.UtcNow,
            AuthorUserId = userId,
        };

        _dataStore.AddNote(notebook.TerminalId, entry);
        _sawmill.Debug($"Note added to terminal '{notebook.TerminalId}' by '{authorName}'.");

        // Push updated state to all viewers
        PushNotesState(uid, notebook, msg.Actor);
    }

    /// <summary>
    /// Client requests deletion of a note. Only the note's author may delete it.
    /// </summary>
    private void OnDeleteNote(EntityUid uid, TerminalNotebookComponent notebook, DeleteTerminalNoteMessage msg)
    {
        var requesterId = GetUserId(msg.Actor);

        // Deny if we can't identify the requester
        if (requesterId == null)
            return;

        // Find the note to verify ownership
        var notes = _dataStore.GetNotes(notebook.TerminalId);
        var note = notes.Find(n => n.Id == msg.NoteId);
        if (note == null)
            return;

        // Only the original author may delete their note
        if (note.AuthorUserId != requesterId)
        {
            _sawmill.Warning($"Player {requesterId} attempted to delete note {msg.NoteId} they do not own.");
            return;
        }

        _dataStore.RemoveNote(notebook.TerminalId, msg.NoteId);
        _sawmill.Debug($"Note {msg.NoteId} deleted from terminal '{notebook.TerminalId}'.");

        // Push updated state to all viewers
        PushNotesState(uid, notebook, msg.Actor);
    }

    // ── Public API for HolotapeSystem integration ────────────────────────────

    /// <summary>
    /// Builds and pushes the full BUI state including notes for this terminal.
    /// Called by both this system's message handlers and HolotapeSystem on UI open.
    /// </summary>
    public void PushNotesState(EntityUid uid, TerminalNotebookComponent notebook, EntityUid actor)
    {
        if (!_ui.HasUi(uid, HolotapeUiKey.Key))
            return;

        // Get the terminal's data content (title/body from HolotapeDataComponent)
        var title = string.Empty;
        var content = string.Empty;
        if (TryComp<HolotapeDataComponent>(uid, out var data))
        {
            title = data.Title;
            content = data.Content;
        }

        var notes = _dataStore.GetNotes(notebook.TerminalId);
        var viewerId = GetUserId(actor);

        var state = new HolotapeBoundUserInterfaceState(title, content, notes, viewerId);
        _ui.SetUiState(uid, HolotapeUiKey.Key, state);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the NetUserId from an actor entity via ActorComponent.
    /// </summary>
    private NetUserId? GetUserId(EntityUid actor)
    {
        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return null;
        return actorComp.PlayerSession.UserId;
    }
}
