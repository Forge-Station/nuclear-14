using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;


namespace Content.Shared._NC.Trade;


public sealed class NcContractGhostRolePerksSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NcContractGhostRolePerksComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NcContractGhostRolePerksComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NcContractGhostRolePerksComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<NcContractGhostRolePerksComponent, DamageModifyEvent>(OnIncomingDamage);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnStartup(Entity<NcContractGhostRolePerksComponent> ent, ref ComponentStartup args) =>
        _movement.RefreshMovementSpeedModifiers(ent.Owner);

    private void OnShutdown(Entity<NcContractGhostRolePerksComponent> ent, ref ComponentShutdown args) =>
        _movement.RefreshMovementSpeedModifiers(ent.Owner);

    private void OnRefreshMovement(
        Entity<NcContractGhostRolePerksComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args
    ) =>
        args.ModifySpeed(ent.Comp.WalkSpeedMultiplier, ent.Comp.SprintSpeedMultiplier, true);

    private void OnIncomingDamage(
        Entity<NcContractGhostRolePerksComponent> ent,
        ref DamageModifyEvent args
    )
    {
        if (ent.Comp.IncomingFlatReductions.Count > 0)
        {
            args.Damage = DamageSpecifier.ApplyModifierSet(
                args.Damage,
                new() { FlatReduction = ent.Comp.IncomingFlatReductions, });
        }

        if (!MathHelper.CloseTo(ent.Comp.IncomingDamageMultiplier, 1f))
            args.Damage *= ent.Comp.IncomingDamageMultiplier;

        if (!MathHelper.CloseTo(ent.Comp.ArmorIncomingDamageMultiplier, 1f) &&
            ent.Comp.ArmorItemPrototypes.Count > 0 &&
            HasMatchingCarriedItem(ent.Owner, ent.Comp.ArmorItemPrototypes))
            args.Damage *= ent.Comp.ArmorIncomingDamageMultiplier;
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit ||
            !TryComp(args.User, out NcContractGhostRolePerksComponent? perks) ||
            MathHelper.CloseTo(perks.MeleeDamageMultiplier, 1f) ||
            !WeaponMatches(ent.Owner, perks.WeaponPrototypes))
            return;

        args.BonusDamage += args.BaseDamage * (perks.MeleeDamageMultiplier - 1f);
    }

    private void OnProjectileHit(Entity<ProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Shooter is not { } shooter ||
            !TryComp(shooter, out NcContractGhostRolePerksComponent? perks) ||
            MathHelper.CloseTo(perks.ProjectileDamageMultiplier, 1f) ||
            !ProjectileWeaponMatches(ent.Comp.Weapon, shooter, perks.WeaponPrototypes))
            return;

        args.Damage *= perks.ProjectileDamageMultiplier;
    }

    private bool ProjectileWeaponMatches(EntityUid? weapon, EntityUid shooter, IReadOnlyCollection<string> allowedPrototypes)
    {
        if (allowedPrototypes.Count == 0)
            return true;

        if (weapon is { } weaponUid)
            return EntityPrototypeMatches(weaponUid, allowedPrototypes);

        return HasMatchingActiveHandItem(shooter, allowedPrototypes);
    }

    private bool WeaponMatches(EntityUid? weapon, IReadOnlyCollection<string> allowedPrototypes)
    {
        if (allowedPrototypes.Count == 0)
            return true;

        return weapon is { } uid && EntityPrototypeMatches(uid, allowedPrototypes);
    }

    private bool HasMatchingCarriedItem(EntityUid uid, IReadOnlyCollection<string> prototypes)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
            if (EntityPrototypeMatches(held, prototypes))
                return true;

        if (!_inventory.TryGetContainerSlotEnumerator((uid, null), out var slots))
            return false;

        while (slots.NextItem(out var item, out _))
            if (EntityPrototypeMatches(item, prototypes))
                return true;

        return false;
    }

    private bool HasMatchingActiveHandItem(EntityUid uid, IReadOnlyCollection<string> prototypes) =>
        _hands.TryGetActiveItem(uid, out var item) &&
        item is { } itemUid &&
        EntityPrototypeMatches(itemUid, prototypes);

    private bool EntityPrototypeMatches(EntityUid uid, IReadOnlyCollection<string> prototypes) =>
        TryComp(uid, out MetaDataComponent? meta) &&
        meta.EntityPrototype is { } proto &&
        prototypes.Contains(proto.ID);
}
