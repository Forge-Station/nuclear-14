using Content.Shared._Forge.Stamina;
using Robust.Shared.Timing;

namespace Content.Server._Forge.ChemEffects;

/// <summary>
///     Applies a temporary multiplier to heavy melee stamina costs while a reagent
///     is metabolized. The smallest active modifier wins; restores 1 when all
///     sources expire.
/// </summary>
public sealed class MeleeStaminaCostModifierSystem : EntitySystem
{
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromSeconds(10);

    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MeleeStaminaCostModifierComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!Prune(comp.Sources, now))
                continue;

            if (comp.Sources.Count == 0)
            {
                RemComp<MeleeStaminaCostModifierComponent>(uid);
                continue;
            }

            Recalculate(uid, comp);
        }
    }

    /// <summary>
    ///     Registers (or refreshes) a source's melee stamina cost modifier.
    /// </summary>
    public void Refresh(EntityUid uid, string source, float modifier)
    {
        if (modifier <= 0f || float.IsNaN(modifier) || float.IsInfinity(modifier))
            return;

        var comp = EnsureComp<MeleeStaminaCostModifierComponent>(uid);
        comp.Sources[source] = (modifier, _timing.CurTime + ExpiryGrace);
        Recalculate(uid, comp);
    }

    private void Recalculate(EntityUid uid, MeleeStaminaCostModifierComponent comp)
    {
        var min = 1f;
        foreach (var (_, entry) in comp.Sources)
            min = MathF.Min(min, entry.Modifier);

        comp.AppliedModifier = min;
        Dirty(uid, comp);
    }

    private static bool Prune(Dictionary<string, (float Modifier, TimeSpan Expiry)> sources, TimeSpan now)
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
}
