using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;

namespace Content.Server._Forge.Radiation;

public sealed partial class N14RadiationChildSystem : EntitySystem
{
    private static readonly string[] AffectedTypes = { "Blunt", "Slash", "Piercing", "Heat", "Shock" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<N14RadiationChildComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(EntityUid uid, N14RadiationChildComponent comp, DamageModifyEvent args)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        if (damageable.TotalDamage >= comp.MaxTotalDamage)
            return;

        if (!damageable.Damage.DamageDict.TryGetValue("Radiation", out var radiation)
            || radiation <= FixedPoint2.Zero)
            return;

        var reduction = FixedPoint2.New(radiation.Float() * comp.ConversionFactor);
        if (reduction <= FixedPoint2.Zero)
            return;

        var damage = new DamageSpecifier(args.Damage);
        var changed = false;
        foreach (var type in AffectedTypes)
        {
            if (!damage.DamageDict.TryGetValue(type, out var value) || value <= FixedPoint2.Zero)
                continue;

            var reduced = FixedPoint2.Max(FixedPoint2.Zero, value - reduction);
            if (reduced == value)
                continue;

            damage.DamageDict[type] = reduced;
            changed = true;
        }

        if (changed)
            args.Damage = damage;
    }
}
