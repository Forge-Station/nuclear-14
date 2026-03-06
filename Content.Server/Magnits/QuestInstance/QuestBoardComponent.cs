using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server.Magnits.QuestInstance;

[RegisterComponent]
public sealed partial class QuestBoardComponent : Component
{
    [DataField]
    public bool HasActiveInstance;

    [DataField]
    public HashSet<NetUserId> Participants = new();

    [DataField]
    public HashSet<EntityUid> PresentPlayers = new();

    [DataField]
    public Dictionary<EntityUid, EntityCoordinates> ReturnCoords = new();

    [DataField]
    public HashSet<int> SentWarnings = new();

    [DataField]
    public List<EntityCoordinates> PendingBarrierCoords = new();

    [DataField]
    public string BarrierProto = "QuestInvisibleWall";

    [DataField]
    public TimeSpan EndAt;

    [DataField]
    public TimeSpan JoinUntil;

    [DataField]
    public int JoinWindowSeconds;

    [DataField]
    public EntityUid MapUid = EntityUid.Invalid;

    [DataField]
    public EntityCoordinates SpawnCoords = EntityCoordinates.Invalid;

    [DataField]
    public int[] WarningThresholdsSeconds = Array.Empty<int>();
}
