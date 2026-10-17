using Content.Shared.GameTicking.Components;
using Content.Server.Psionics.Glimmer;
using Content.Shared.Psionics.Glimmer;

namespace Content.Server.StationEvents.Events;

public sealed class GlimmerEventSystem : StationEventSystem<GlimmerEventComponent>
{
    [Dependency] private readonly GlimmerSystem _glimmerSystem = default!;

    protected override void Ended(Entity<GlimmerEventComponent> rule, ref GameRuleEndedEvent args)
    {
        base.Ended(rule, ref args); // Forge-Change: у Ended теперь Entity<T> и ByRef-событие

        var glimmerBurned = RobustRandom.Next(rule.Comp.GlimmerBurnLower, rule.Comp.GlimmerBurnUpper);
        _glimmerSystem.DeltaGlimmerInput(-glimmerBurned);

        var reportEv = new GlimmerEventEndedEvent(rule.Comp.SophicReport, glimmerBurned);
        RaiseLocalEvent(reportEv);
    }
}

public sealed class GlimmerEventEndedEvent : EntityEventArgs
{
    public string Message = "";
    public int GlimmerBurned = 0;

    public GlimmerEventEndedEvent(string message, int glimmerBurned)
    {
        Message = message;
        GlimmerBurned = glimmerBurned;
    }
}
