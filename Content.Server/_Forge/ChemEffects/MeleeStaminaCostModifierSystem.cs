using Content.Shared._Forge.Weapons.Melee;
using Robust.Shared.Timing;

namespace Content.Server._Forge.ChemEffects;

/// <summary>
///     Applies a temporary multiplier to heavy melee stamina costs while a reagent
///     is metabolized. The smallest active chem modifier wins against the pre-chem
///     base (e.g. perk); restores that base when all chem sources expire.
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
                RestoreBase(uid, comp);
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

        if (!comp.ChemInitialized)
        {
            comp.ChemInitialized = true;
            comp.BaseHeavyCostMultiplier = comp.HeavyCostMultiplier;
        }

        comp.Sources[source] = (modifier, _timing.CurTime + ExpiryGrace);
        Recalculate(uid, comp);
    }

    private void Recalculate(EntityUid uid, MeleeStaminaCostModifierComponent comp)
    {
        // Smallest wins among perk/base and active chems (no multiplicative stack).
        var min = comp.BaseHeavyCostMultiplier;
        foreach (var (_, entry) in comp.Sources)
            min = MathF.Min(min, entry.Modifier);

        comp.HeavyCostMultiplier = min;
        Dirty(uid, comp);
    }

    private void RestoreBase(EntityUid uid, MeleeStaminaCostModifierComponent comp)
    {
        if (comp.ChemInitialized)
        {
            comp.HeavyCostMultiplier = comp.BaseHeavyCostMultiplier;
            comp.ChemInitialized = false;
            Dirty(uid, comp);
        }

        // Keep the component if a perk (or other non-default base) still needs it.
        if (comp.HeavyCostMultiplier.Equals(1f))
            RemComp<MeleeStaminaCostModifierComponent>(uid);
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
