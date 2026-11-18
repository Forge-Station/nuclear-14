// #Forge-Change
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Roles.Ranks;

[Serializable, NetSerializable]
public sealed class RequestRankPreferencesEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class SetRankPreferenceEvent(string job, string? rank) : EntityEventArgs
{
    public readonly string Job = job;
    public readonly string? Rank = rank;
}

[Serializable, NetSerializable]
public sealed class RankPreferencesEvent(Dictionary<string, string> preferences) : EntityEventArgs
{
    public readonly Dictionary<string, string> Preferences = preferences;
}
