using Content.Shared.DoAfter;
using Robust.Shared.Timing;

namespace Content.Server._Forge.DoAfterSpeed;

/// <summary>
///     Applies temporary DoAfter speed modifiers to a mob while source reagents are
///     being metabolized. For each DoAfter event type the highest active speed is used
///     (speed &gt; 1 = faster, &lt; 1 = slower). Multiple sources may be active at once;
///     per event type only the highest speed is applied, and lower ones take over once
///     the highest expires. Sources are only driven on the server (via metabolism), so
///     this system runs server-side only.
/// </summary>
public sealed partial class DoAfterSpeedModifierSystem : EntitySystem
{
    /// <summary>
    ///     How long a source remains valid after its last metabolism tick.
    /// </summary>
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromSeconds(10);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DoAfterSpeedModifierComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            Prune(comp.Sources, now);

            _doAfter.ApplyDoAfterSpeedModifiers(uid, comp.Sources, comp.BaseDelays);

            if (comp.Sources.Count == 0 && comp.BaseDelays.Count == 0)
                RemComp<DoAfterSpeedModifierComponent>(uid);
        }
    }

    /// <summary>
    ///     Registers (or refreshes) a source's speed multipliers. Called once per metabolism tick.
    /// </summary>
    public void Refresh(EntityUid uid, string source, Dictionary<string, float> speeds)
    {
        var comp = EnsureComp<DoAfterSpeedModifierComponent>(uid);
        var expiry = _timing.CurTime + ExpiryGrace;

        foreach (var (eventType, speed) in speeds)
        {
            if (speed <= 0f || float.IsNaN(speed) || float.IsInfinity(speed))
                continue;

            if (!comp.Sources.TryGetValue(eventType, out var sub))
                comp.Sources[eventType] = sub = new();

            sub[source] = (speed, expiry);
        }
    }

    private static void Prune(Dictionary<string, Dictionary<string, (float Speed, TimeSpan Expiry)>> sources,
        TimeSpan now)
    {
        List<string>? emptyEventTypes = null;

        foreach (var (eventType, sub) in sources)
        {
            List<string>? expired = null;
            foreach (var (source, entry) in sub)
            {
                if (entry.Expiry >= now)
                    continue;

                expired ??= new List<string>();
                expired.Add(source);
            }

            if (expired != null)
            {
                foreach (var source in expired)
                    sub.Remove(source);
            }

            if (sub.Count != 0)
                continue;

            emptyEventTypes ??= new List<string>();
            emptyEventTypes.Add(eventType);
        }

        if (emptyEventTypes == null)
            return;

        foreach (var eventType in emptyEventTypes)
            sources.Remove(eventType);
    }
}
