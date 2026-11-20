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
        {
            Log.Debug($"ModifyMobThresholds: {ToPrettyString(uid)} has no MobThresholdsComponent, skipping");
            return;
        }

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

        Log.Debug($"ModifyMobThresholds refresh {ToPrettyString(uid)} source='{source}' crit={crit} death={death} " +
                  $"base=({comp.BaseCrit},{comp.BaseDead})");

        Recalculate(uid, comp);
    }

    private static bool Prune(Dictionary<string, (int Modifier, TimeSpan Expiry)> sources, TimeSpan now)
    {
        var removed = false;
        foreach (var (key, entry) in sources)
        {
            if (entry.Expiry < now)
            {
                sources.Remove(key);
                removed = true;
            }
        }

        return removed;
    }

    private void Recalculate(EntityUid uid, MobThresholdModifierComponent comp)
    {
        if (!HasComp<MobThresholdsComponent>(uid))
            return;

        ApplyThreshold(uid, MobState.Critical, comp.BaseCrit, ref comp.AppliedCrit, MaxModifier(comp.CritSources));
        ApplyThreshold(uid, MobState.Dead, comp.BaseDead, ref comp.AppliedDead, MaxModifier(comp.DeadSources));

        if (comp.CritSources.Count == 0 && comp.DeadSources.Count == 0)
            RemComp<MobThresholdModifierComponent>(uid);
    }

    private void ApplyThreshold(
        EntityUid uid,
        MobState mobState,
        FixedPoint2 baseThreshold,
        ref FixedPoint2 applied,
        int modifier)
    {
        // No base threshold => the mob doesn't have this state; nothing to modify or restore.
        if (baseThreshold == FixedPoint2.Zero)
            return;

        var desired = baseThreshold + modifier;
        if (applied == desired)
            return;

        _threshold.SetMobStateThreshold(uid, desired, mobState);
        applied = desired;

        Log.Debug($"ModifyMobThresholds: {ToPrettyString(uid)} set {mobState} threshold {desired}");
    }

    private static int MaxModifier(Dictionary<string, (int Modifier, TimeSpan Expiry)> sources)
    {
        var max = 0;
        foreach (var (_, entry) in sources)
            max = Math.Max(max, entry.Modifier);

        return max;
    }
}
