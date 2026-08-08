using Content.Server.UserInterface;
using Content.Shared._Misfits.Holotape;
using Content.Shared.Dataset;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

// #Misfits Add - Server system for holotape reading and terminal random content.
// Handles: holotape insertion into readers, terminal click-to-read, MapInit content setup.
// #Misfits Add - Integrates with TerminalNotebookSystem to include notes in terminal state.

namespace Content.Server._Misfits.Holotape;

/// <summary>
/// Manages holotape playback and terminal content display.
/// Holotapes require a reader (terminal/Pip-Boy) to view their content.
/// Terminals display randomized pre-war/post-war entries when clicked directly.
/// </summary>
public sealed class HolotapeSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    // #Misfits Add - Notebook system dependency for notes integration on terminal UI open
    [Dependency] private readonly TerminalNotebookSystem _notebook = default!;
    [Dependency] private readonly TerminalNotesDataStore _notesData = default!;

    // Tracks which terminal entry keys have been assigned this round.
    // Ensures no two terminals display the same content.
    private readonly HashSet<string> _usedEntries = new();

    public override void Initialize()
    {
        base.Initialize();

        // Clear used entry tracking on round restart
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        // Localize holotape/terminal content on map spawn
        SubscribeLocalEvent<HolotapeDataComponent, MapInitEvent>(OnHolotapeDataMapInit);

        // Pick random content for terminals on map spawn
        SubscribeLocalEvent<TerminalRandomContentComponent, MapInitEvent>(OnTerminalRandomMapInit);

        // When a holotape is used on a reader entity, open the viewer
        SubscribeLocalEvent<HolotapeReaderComponent, InteractUsingEvent>(OnReaderInteractUsing);

        // When a terminal with its own content is clicked, show that content
        SubscribeLocalEvent<HolotapeDataComponent, AfterActivatableUIOpenEvent>(OnTerminalUIOpen);
    }

    /// <summary>
    /// Resets used entry tracking so the next round gets a fresh pool.
    /// </summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _usedEntries.Clear();
    }

    /// <summary>
    /// Resolves FTL locale keys to actual strings on map initialization.
    /// </summary>
    private void OnHolotapeDataMapInit(EntityUid uid, HolotapeDataComponent comp, MapInitEvent args)
    {
        if (comp.Localized)
            return;

        // Resolve title FTL key
        if (!string.IsNullOrEmpty(comp.Title))
            comp.Title = Loc.GetString(comp.Title);

        // Resolve content FTL key
        if (!string.IsNullOrEmpty(comp.Content))
            comp.Content = Loc.GetString(comp.Content);

        comp.Localized = true;
    }

    /// <summary>
    /// Picks a random entry from the configured dataset pool and writes it
    /// into the entity's HolotapeDataComponent before localization runs.
    /// Each dataset value is an FTL key prefix; "-title" and "-content" are appended.
    /// </summary>
    private void OnTerminalRandomMapInit(EntityUid uid, TerminalRandomContentComponent comp, MapInitEvent args)
    {
        if (!TryComp<HolotapeDataComponent>(uid, out var data))
            return;

        if (comp.ContentPool.Count == 0)
            return;

        // Pick a random dataset ID from the pool
        var datasetId = _random.Pick(comp.ContentPool);

        if (!_prototype.TryIndex<DatasetPrototype>(datasetId, out var dataset))
            return;

        if (dataset.Values.Count == 0)
            return;

        // Filter out entries already assigned to other terminals this round
        var available = new List<string>();
        foreach (var val in dataset.Values)
        {
            if (!_usedEntries.Contains(val))
                available.Add(val);
        }

        // If all entries are used (more terminals than entries), reset and allow repeats
        if (available.Count == 0)
            available.AddRange(dataset.Values);

        // Pick a unique random FTL key prefix from the available pool
        var entryKey = _random.Pick(available);
        _usedEntries.Add(entryKey);

        // Set title and content using the key prefix + suffixes
        data.Title = entryKey + "-title";
        data.Content = entryKey + "-content";

        // #Misfits Fix - Localize immediately here because OnHolotapeDataMapInit fires
        // BEFORE this handler (subscription order) and has already set Localized = true.
        data.Title = Loc.GetString(data.Title);
        data.Content = Loc.GetString(data.Content);
        data.Localized = true;
    }

    /// <summary>
    /// When a holotape is used on a reader (terminal or Pip-Boy),
    /// open the holotape's own terminal viewer UI and display its content.
    /// Uses the holotape entity's UserInterface, not the reader's.
    /// </summary>
    private void OnReaderInteractUsing(EntityUid uid, HolotapeReaderComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Check if the used item is a holotape with content
        if (!TryComp<HolotapeDataComponent>(args.Used, out var holotapeData))
            return;

        // Open the holotape's own UI (the holotape entity has UserInterface + HolotapeUiKey)
        if (!_ui.HasUi(args.Used, HolotapeUiKey.Key))
            return;

        _ui.OpenUi(args.Used, HolotapeUiKey.Key, args.User);
        // #Misfits Add - Pass isHolotapeItem: true so client shows HOLOTAPE header instead of TERMINAL
        _ui.SetUiState(args.Used, HolotapeUiKey.Key,
            new HolotapeBoundUserInterfaceState(holotapeData.Title, holotapeData.Content, isHolotapeItem: true));

        args.Handled = true;
    }

    /// <summary>
    /// When a terminal with its own HolotapeDataComponent is clicked and
    /// the ActivatableUI opens, send the terminal's own content to the viewer.
    /// If the terminal has a TerminalNotebookComponent, include persisted notes.
    /// </summary>
    private void OnTerminalUIOpen(EntityUid uid, HolotapeDataComponent comp, AfterActivatableUIOpenEvent args)
    {
        if (!_ui.HasUi(uid, HolotapeUiKey.Key))
            return;

        // #Misfits Add - Include notebook notes and viewer ID if terminal has notebook
        if (TryComp<TerminalNotebookComponent>(uid, out var notebook)
            && !string.IsNullOrEmpty(notebook.TerminalId))
        {
            var notes = _notesData.GetNotes(notebook.TerminalId);
            NetUserId? viewerId = null;
            if (TryComp<ActorComponent>(args.User, out var actor))
                viewerId = actor.PlayerSession.UserId;

            _ui.SetUiState(uid, HolotapeUiKey.Key,
                new HolotapeBoundUserInterfaceState(comp.Title, comp.Content, notes, viewerId));
            return;
        }

        _ui.SetUiState(uid, HolotapeUiKey.Key,
            new HolotapeBoundUserInterfaceState(comp.Title, comp.Content));
    }
}
