using Content.Shared.Movement.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared._N14.Radiation.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Damage.Systems;

namespace Content.Shared._N14.Radiation;

public sealed partial class RadiationHealingSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private static readonly string[] HealableTypes = { "Blunt", "Slash", "Piercing", "Heat" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadiationHealingComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMove);
    }

    private void OnRefreshMove(EntityUid uid, RadiationHealingComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (component.CurrentExposure <= 0f)
            return;

        if (!TryComp(uid, out DamageableComponent? damage)
            || _damageable.GetTotalDamage((uid, damage)) >= 90f)
            return;

        var healable = GetHealableDamage((uid, damage));
        if (healable <= 0f)
            return;

        var mod = Math.Clamp(1f - component.CurrentExposure * component.SlowFactor, 0.5f, 1f);
        args.ModifySpeed(mod);
    }

    private float GetHealableDamage(Entity<DamageableComponent> damage)
    {
        var dict = _damageable.GetAllDamage(damage).DamageDict;
        float amount = 0f;
        foreach (var type in HealableTypes)
        {
            if (dict.TryGetValue(type, out var val) && val > FixedPoint2.Zero)
                amount += val.Float();
        }

        return amount;
    }
}
