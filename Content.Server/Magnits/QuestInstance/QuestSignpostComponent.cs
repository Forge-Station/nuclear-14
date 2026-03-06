namespace Content.Server.Magnits.QuestInstance;

/// <summary>
/// Spawned near quest instance entry.
/// Interacting teleports player back via QuestInstanceSystem.
/// </summary>
[RegisterComponent]
public sealed partial class QuestSignpostComponent : Component
{
    /// <summary>
    /// The QuestBoard entity that owns this instance.
    /// </summary>
    [DataField]
    public EntityUid BoardUid;
}
