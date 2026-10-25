using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Radiation;

public sealed partial class N14RadiationChildSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly string[] AffectedTypes = { "Blunt", "Slash", "Piercing", "Heat", "Shock" };

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<N14RadiationChildComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damageable))
        {
            if (now < comp.NextHeal)
                continue;

            comp.NextHeal = now + TimeSpan.FromSeconds(comp.Interval);

            if (damageable.TotalDamage >= comp.MaxTotalDamage)
                continue;

            if (!damageable.Damage.DamageDict.TryGetValue("Radiation", out var radiation)
                || radiation <= FixedPoint2.Zero)
                continue;

            var heal = FixedPoint2.New(radiation.Float() * comp.ConversionFactor);
            if (heal <= FixedPoint2.Zero)
                continue;

            var damage = damageable.Damage;
            var changed = false;
            foreach (var type in AffectedTypes)
            {
                if (!damage.DamageDict.TryGetValue(type, out var value) || value <= FixedPoint2.Zero)
                    continue;

                var healed = FixedPoint2.Max(FixedPoint2.Zero, value - heal);
                if (healed == value)
                    continue;

                damage.DamageDict[type] = healed;
                changed = true;
            }

            if (changed)
                _damageable.SetDamage(uid, damageable, damage);
        }
    }
}
