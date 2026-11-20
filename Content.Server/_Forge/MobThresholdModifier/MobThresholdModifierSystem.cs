using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.MobThresholdModifier;

/// <summary>
///     Applies temporary damage threshold modifiers to mobs while a source reagent is
///     being metabolized. "Crit" adds to the Critical threshold, "death" adds to the
///     death threshold. Multiple sources may be active at once; per group only the
///     largest modifier is applied, and smaller ones take over once the largest expires.
///     If Crit would meet or exceed Dead, Crit is clamped just below Dead so the two
///     states never share a SortedDictionary key (Psycho-style "skip crit").
/// </summary>
public sealed class MobThresholdModifierSystem : EntitySystem
{
    /// <summary>
    ///     How long a source remains valid after its last metabolism tick.
    /// </summary>
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromSeconds(10);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobThresholdSystem _threshold = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MobThresholdModifierComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var pruned = Prune(comp.CritSources, now) | Prune(comp.DeadSources, now);
            if (pruned)
                Recalculate(uid, comp);
        }
    }

    /// <summary>
    ///     Registers (or refreshes) a source's modifiers. Called once per metabolism tick.
    /// </summary>
    public void Refresh(EntityUid uid, string source, int crit, int death)
    {
        if (crit == 0 && death == 0 && !HasComp<MobThresholdModifierComponent>(uid))
            return;

        if (!HasComp<MobThresholdsComponent>(uid))
            return;

        var comp = EnsureComp<MobThresholdModifierComponent>(uid);

        if (comp.CritSources.Count == 0 && comp.DeadSources.Count == 0)
        {
            comp.BaseCrit = _threshold.GetThresholdForState(uid, MobState.Critical);
            comp.BaseDead = _threshold.GetThresholdForState(uid, MobState.Dead);

            comp.AppliedCrit = comp.BaseCrit;
            comp.AppliedDead = comp.BaseDead;
        }

        var expiry = _timing.CurTime + ExpiryGrace;
        if (crit != 0)
            comp.CritSources[source] = (crit, expiry);
        if (death != 0)
            comp.DeadSources[source] = (death, expiry);

        Recalculate(uid, comp);
    }

    private static bool Prune(Dictionary<string, (int Modifier, TimeSpan Expiry)> sources, TimeSpan now)
    {
        List<string>? expired = null;
        foreach (var (key, entry) in sources)
        {
            if (entry.Expiry >= now)
                continue;

            expired ??= new List<string>();
            expired.Add(key);
        }

        if (expired == null)
            return false;

        foreach (var key in expired)
            sources.Remove(key);

        return true;
    }

    private void Recalculate(EntityUid uid, MobThresholdModifierComponent comp)
    {
        if (!HasComp<MobThresholdsComponent>(uid))
            return;

        var critMod = MaxModifier(comp.CritSources);
        var deadMod = MaxModifier(comp.DeadSources);

        var desiredCrit = comp.BaseCrit == FixedPoint2.Zero
            ? FixedPoint2.Zero
            : comp.BaseCrit + critMod;
        var desiredDead = comp.BaseDead == FixedPoint2.Zero
            ? FixedPoint2.Zero
            : comp.BaseDead + deadMod;

        // Thresholds is a SortedDictionary keyed by damage — Crit and Dead cannot share a key.
        // Clamp Crit just below Dead so Psycho (crit+100 on a 100/200 mob) stays valid.
        if (desiredCrit != FixedPoint2.Zero && desiredDead != FixedPoint2.Zero && desiredCrit >= desiredDead)
            desiredCrit = FixedPoint2.Max(FixedPoint2.New(1), desiredDead - FixedPoint2.New(1));

        ApplyThreshold(uid, MobState.Critical, ref comp.AppliedCrit, desiredCrit);
        ApplyThreshold(uid, MobState.Dead, ref comp.AppliedDead, desiredDead);

        if (comp.CritSources.Count == 0 && comp.DeadSources.Count == 0)
            RemComp<MobThresholdModifierComponent>(uid);
    }

    private void ApplyThreshold(
        EntityUid uid,
        MobState mobState,
        ref FixedPoint2 applied,
        FixedPoint2 desired)
    {
        // No base threshold => the mob doesn't have this state; nothing to modify or restore.
        if (desired == FixedPoint2.Zero)
            return;

        if (applied == desired)
            return;

        _threshold.SetMobStateThreshold(uid, desired, mobState);
        applied = desired;
    }

    private static int MaxModifier(Dictionary<string, (int Modifier, TimeSpan Expiry)> sources)
    {
        var max = 0;
        foreach (var (_, entry) in sources)
            max = Math.Max(max, entry.Modifier);

        return max;
    }
}
