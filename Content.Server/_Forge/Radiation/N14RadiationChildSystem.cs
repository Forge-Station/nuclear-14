using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;

namespace Content.Server._Forge.Radiation;

public sealed partial class N14RadiationChildSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private static readonly string[] HealableTypes = { "Blunt", "Slash", "Piercing", "Heat" };

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<N14RadiationChildComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damageable))
        {
            if (damageable.TotalDamage >= comp.MaxTotalDamage)
                continue;

            if (!damageable.Damage.DamageDict.TryGetValue("Radiation", out var radiation)
                || radiation <= FixedPoint2.Zero)
                continue;

            var convert = FixedPoint2.Min(radiation, FixedPoint2.New(comp.RadsPerSecond * frameTime));
            var heal = FixedPoint2.New(convert.Float() * comp.HealPerTrigger);

            var damage = damageable.Damage;
            foreach (var type in HealableTypes)
            {
                if (damage.DamageDict.TryGetValue(type, out var value))
                    damage.DamageDict[type] = FixedPoint2.Max(FixedPoint2.Zero, value - heal);
            }

            _damageable.SetDamage(uid, damageable, damage);
        }
    }
}
