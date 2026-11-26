using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Radiation;

public sealed partial class N14RadiationChildSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly string[] HealedTypes = { "Blunt", "Slash", "Piercing", "Heat", "Shock" };

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<N14RadiationChildComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damageable))
        {
            if (now < comp.NextHeal)
                continue;

            comp.NextHeal = now + TimeSpan.FromSeconds(comp.Interval);

            if (!damageable.Damage.DamageDict.TryGetValue("Radiation", out var radiation)
                || radiation <= FixedPoint2.Zero)
                continue;

            // Radiation is the fuel. Counting it toward the cap turns the perk off
            // as soon as there is enough of it to matter.
            var injury = damageable.TotalDamage - radiation;
            if (injury >= comp.MaxTotalDamage)
                continue;

            var heal = FixedPoint2.New(radiation.Float() * comp.ConversionFactor);
            if (heal <= FixedPoint2.Zero)
                continue;

            HealDamage(uid, damageable, heal);

            if (!TryComp<BodyComponent>(uid, out var body))
                continue;

            foreach (var type in HealedTypes)
            {
                var remaining = heal;
                foreach (var (partId, _) in _body.GetBodyChildren(uid, body))
                {
                    if (remaining <= FixedPoint2.Zero)
                        break;

                    if (!TryComp<DamageableComponent>(partId, out var partDamage)
                        || !partDamage.Damage.DamageDict.TryGetValue(type, out var value)
                        || value <= FixedPoint2.Zero)
                        continue;

                    var amount = FixedPoint2.Min(value, remaining);
                    var spec = new DamageSpecifier();
                    spec.DamageDict[type] = -amount;
                    _damageable.TryChangeDamage(
                        partId,
                        spec,
                        ignoreResistances: true,
                        interruptsDoAfters: false,
                        doPartDamage: false,
                        canSever: false);
                    remaining -= amount;
                }
            }
        }
    }

    /// <summary>
    ///     Heals the mob's own damage container. Limb damage is applied separately,
    ///     because weapon and reagent damage in this fork is stored on body parts.
    /// </summary>
    private void HealDamage(EntityUid uid, DamageableComponent damageable, FixedPoint2 amount)
    {
        var spec = new DamageSpecifier();
        foreach (var type in HealedTypes)
        {
            if (!damageable.Damage.DamageDict.TryGetValue(type, out var value) || value <= FixedPoint2.Zero)
                continue;

            spec.DamageDict[type] = -FixedPoint2.Min(value, amount);
        }

        if (spec.Empty)
            return;

        _damageable.TryChangeDamage(
            uid,
            spec,
            ignoreResistances: true,
            interruptsDoAfters: false,
            doPartDamage: false,
            canSever: false);
    }
}
