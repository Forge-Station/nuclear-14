using Robust.Shared.Serialization;

namespace Content.Shared.GameTicking;

[Serializable, NetSerializable]
public sealed class TickerJobRestrictionsEvent : EntityEventArgs
{
    public HashSet<string>? RestrictedJobs { get; }

    public TickerJobRestrictionsEvent(HashSet<string>? restrictedJobs)
    {
        RestrictedJobs = restrictedJobs;
    }
}
