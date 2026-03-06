namespace Content.Server.Magnits.QuestInstance;

/// <summary>
/// Placed at the center of a quest instance (tile 0,0).
/// When a player interacts with this entity, QuestInstanceSystem teleports them
/// back to their saved origin and removes them from the instance.
/// </summary>
[RegisterComponent]
public sealed partial class QuestSignpostComponent : Component
{
    /// <summary>
    /// The QuestBoard entity (with <see cref="QuestBoardComponent"/>) that spawned this instance.
    /// Used to look up the active <c>QuestInstance</c> in QuestInstanceSystem.
    /// </summary>
    [DataField]
    public EntityUid BoardUid;
}
