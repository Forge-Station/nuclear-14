using Robust.Shared.Serialization;

namespace Content.Shared.Magnits.QuestInstance;

/// <summary>Difficulty tiers for quest instances.</summary>
[Serializable, NetSerializable]
public enum QuestDifficulty { Easy, Medium, Hard }

/// <summary>BUI key — one entry per QuestBoard entity.</summary>
[Serializable, NetSerializable]
public enum QuestBoardUiKey { Key }

// ──────────────────────────── State ────────────────────────────────────────

/// <summary>
/// Sent from server to client whenever the board state changes or the UI opens.
/// </summary>
[Serializable, NetSerializable]
public sealed class QuestBoardBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>True when an instance is currently active for this board.</summary>
    public bool HasActiveInstance;

    /// <summary>Seconds until force-close. Only meaningful when HasActiveInstance.</summary>
    public int RemainingSeconds;

    /// <summary>Number of players physically present in the instance.</summary>
    public int ParticipantCount;

    public QuestBoardBoundUserInterfaceState(
        bool hasActiveInstance,
        int remainingSeconds,
        int participantCount)
    {
        HasActiveInstance = hasActiveInstance;
        RemainingSeconds  = remainingSeconds;
        ParticipantCount  = participantCount;
    }
}

// ──────────────────────────── Messages ─────────────────────────────────────

/// <summary>
/// Sent from client when the player presses Easy / Medium / Hard
/// (create a new instance) or the Join button (join existing instance;
/// Difficulty field is ignored server-side if an instance already exists).
/// </summary>
[Serializable, NetSerializable]
public sealed class QuestBoardSelectDifficultyMessage : BoundUserInterfaceMessage
{
    public QuestDifficulty Difficulty;

    public QuestBoardSelectDifficultyMessage(QuestDifficulty difficulty)
    {
        Difficulty = difficulty;
    }
}
