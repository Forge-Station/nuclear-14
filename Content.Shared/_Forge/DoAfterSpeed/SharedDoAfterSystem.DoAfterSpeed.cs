using System.Linq;
using Content.Shared.DoAfter;

namespace Content.Shared.DoAfter;

// Forge-add-start: partial extension of SharedDoAfterSystem granting the DoAfter-speed
// module the ability to rewrite the active delay, without touching the core DoAfter files.
public abstract partial class SharedDoAfterSystem
{
    /// <summary>
    ///     For each active DoAfter of <paramref name="user"/>, applies the highest speed
    ///     currently active for its event type (speed &gt; 1 = faster, &lt; 1 = slower).
    ///     Original delays are stored in <paramref name="baseDelays"/> so they can be
    ///     restored once the speed returns to 1. Dirty the DoAfterComponent so the client
    ///     receives the updated delay.
    /// </summary>
    public void ApplyDoAfterSpeedModifiers(
        EntityUid user,
        Dictionary<string, Dictionary<string, (float Speed, TimeSpan Expiry)>> sources,
        Dictionary<DoAfterId, TimeSpan> baseDelays)
    {
        if (!TryComp(user, out DoAfterComponent? doAfterComp))
            return;

        foreach (var doAfter in doAfterComp.DoAfters.Values)
        {
            if (doAfter.Cancelled || doAfter.Completed)
                continue;

            var speed = GetDoAfterSpeed(sources, doAfter.Args.Event.GetType().Name);
            var id = doAfter.Id;

            if (speed != 1f)
            {
                if (!baseDelays.TryGetValue(id, out var baseDelay))
                {
                    baseDelay = doAfter.Args.Delay;
                    baseDelays[id] = baseDelay;
                }

                var newDelay = baseDelay / speed;
                if (doAfter.Args.Delay != newDelay)
                {
                    doAfter.Args.Delay = newDelay;
                    Dirty(user, doAfterComp);
                }
            }
            else if (baseDelays.Remove(id, out var baseDelay))
            {
                doAfter.Args.Delay = baseDelay;
                Dirty(user, doAfterComp);
            }
        }

        // Drop tracking for DoAfters that no longer exist.
        foreach (var key in baseDelays.Keys.Where(k => !doAfterComp.DoAfters.ContainsKey(k.Index)).ToList())
            baseDelays.Remove(key);
    }

    private static float GetDoAfterSpeed(
        Dictionary<string, Dictionary<string, (float Speed, TimeSpan Expiry)>> sources,
        string eventType)
    {
        if (!sources.TryGetValue(eventType, out var sub))
            return 1f;

        var max = float.MinValue;
        foreach (var (_, entry) in sub)
        {
            if (entry.Speed <= 0f || float.IsNaN(entry.Speed) || float.IsInfinity(entry.Speed))
                continue;

            max = MathF.Max(max, entry.Speed);
        }

        return max == float.MinValue ? 1f : max;
    }
}
// Forge-add-end