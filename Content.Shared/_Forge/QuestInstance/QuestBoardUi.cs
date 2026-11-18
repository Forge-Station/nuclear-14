using Robust.Shared.Serialization;


namespace Content.Shared._Forge.QuestInstance;


/// <summary>Difficulty tiers for quest instances.</summary>
[Serializable, NetSerializable,]
public enum QuestDifficulty
{
    Easy,
    Medium,
    Hard
}

/// <summary>BUI key for QuestBoard UI.</summary>
[Serializable, NetSerializable,]
public enum QuestBoardUiKey
{
    Key
}

/// <summary>
///     Sent from server to client whenever board state changes.
/// </summary>
[Serializable, NetSerializable,]
public sealed class QuestBoardBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>True when an instance is active for this board.</summary>
    public bool HasActiveInstance;

    /// <summary>Number of players physically present in the instance.</summary>
    public int ParticipantCount;

    /// <summary>Seconds until force-close. Meaningful only when active.</summary>
    public int RemainingSeconds;

    public QuestBoardBoundUserInterfaceState(bool hasActiveInstance, int remainingSeconds, int participantCount)
    {
        HasActiveInstance = hasActiveInstance;
        RemainingSeconds = remainingSeconds;
        ParticipantCount = participantCount;
    }
}

/// <summary>
///     Sent from client when selecting difficulty or joining an active instance.
///     Difficulty is ignored by server when an instance is already active.
/// </summary>
[Serializable, NetSerializable,]
public sealed class QuestBoardSelectDifficultyMessage : BoundUserInterfaceMessage
{
    public QuestDifficulty Difficulty;

    public QuestBoardSelectDifficultyMessage(QuestDifficulty difficulty)
    {
        Difficulty = difficulty;
    }
}

[RegisterComponent]
public sealed partial class WeatherAudioListenerComponent : Component { }
