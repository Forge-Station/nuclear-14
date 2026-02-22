using Content.Shared.Damage;

namespace Content.Shared.Projectiles;


public sealed class GunDamageModifierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(EntityUid uid, ProjectileComponent projectile, ref ProjectileHitEvent args)
    {
        if (projectile.Weapon is not {} weaponUid ||
            !TryComp<GunDamageModifierComponent>(weaponUid, out var modifier))
            return;

        args.Damage = ApplyModifier(args.Damage, modifier);
    }

    public static DamageSpecifier ApplyModifier(DamageSpecifier original, GunDamageModifierComponent modifier)
    {
        var result = original * modifier.Multiplier;

        if (modifier.FlatBonus is {} flat)
            result += flat;

        return result;
    }
}
