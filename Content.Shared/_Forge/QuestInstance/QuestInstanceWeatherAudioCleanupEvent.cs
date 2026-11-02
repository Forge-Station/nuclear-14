using Robust.Shared.Serialization;

namespace Content.Shared._Forge.QuestInstance;

[Serializable, NetSerializable]
public sealed class QuestInstanceWeatherAudioCleanupEvent(NetEntity? keepMapUid) : EntityEventArgs
{
    public readonly NetEntity? KeepMapUid = keepMapUid;
}
