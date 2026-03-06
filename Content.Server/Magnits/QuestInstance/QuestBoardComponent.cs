using Robust.Shared.GameObjects;

namespace Content.Server.Magnits.QuestInstance;

/// <summary>
/// Marks an entity as a Quest Board.
/// QuestInstanceSystem enforces at most one active instance per board.
/// Call <c>QuestInstanceSystem.StartOrJoinInstance(boardUid, session, difficulty)</c>
/// from a UI or interaction handler to create or join an instance.
/// </summary>
[RegisterComponent]
public sealed partial class QuestBoardComponent : Component { }
