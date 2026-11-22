using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.ChemEffects;

/// <summary>
///     Applies a temporary maximum-stamina multiplier while a reagent is metabolized.
///     The largest active chem modifier wins as an absolute StaminaModifier value;
///     when all sources expire the original StaminaModifier (or its absence) is restored.
/// </summary>
public sealed class TimedStaminaModifierSystem : EntitySystem
{
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromSeconds(10);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TimedStaminaModifierComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!Prune(comp.Sources, now))
                continue;

            if (comp.Sources.Count > 0)
            {
                Apply(uid, comp);
                continue;
            }

            Restore(uid, comp);
        }
    }

    /// <summary>
    ///     Registers (or refreshes) a source's stamina modifier. Called once per metabolism tick.
    /// </summary>
    public void Refresh(EntityUid uid, string source, float modifier)
    {
        if (modifier < 0.1f || float.IsNaN(modifier) || float.IsInfinity(modifier))
            return;

        var comp = EnsureComp<TimedStaminaModifierComponent>(uid);

        if (!comp.Initialized)
        {
            comp.Initialized = true;
            comp.HadModifier = TryComp<StaminaModifierComponent>(uid, out var existing);
            existing ??= EnsureComp<StaminaModifierComponent>(uid);
            comp.BaseModifier = existing.Modifier;
        }

        comp.Sources[source] = (modifier, _timing.CurTime + ExpiryGrace);
        Apply(uid, comp);
    }

    private void Apply(EntityUid uid, TimedStaminaModifierComponent comp)
    {
        var max = 1f;
        foreach (var (_, entry) in comp.Sources)
            max = MathF.Max(max, entry.Modifier);

        // Absolute value from YAML (e.g. 1.5), not stacked on BaseModifier —
        // BaseModifier is only used to restore the pre-chem state in Restore().
        _stamina.SetModifier(uid, max);
    }

    private void Restore(EntityUid uid, TimedStaminaModifierComponent comp)
    {
        if (!TryComp<StaminaModifierComponent>(uid, out _))
        {
            RemComp<TimedStaminaModifierComponent>(uid);
            return;
        }

        if (comp.HadModifier)
        {
            _stamina.SetModifier(uid, comp.BaseModifier);
        }
        else
        {
            _stamina.SetModifier(uid, 1f);
            RemComp<StaminaModifierComponent>(uid);
        }

        RemComp<TimedStaminaModifierComponent>(uid);
    }

    private static bool Prune(Dictionary<string, (float Modifier, TimeSpan Expiry)> sources, TimeSpan now)
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
}
