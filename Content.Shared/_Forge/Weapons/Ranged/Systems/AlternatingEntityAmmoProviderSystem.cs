using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Weapons.Ranged.Systems;

/// <summary>
/// Handles <see cref="AlternatingEntityAmmoProviderComponent"/>.
/// </summary>
public sealed class AlternatingEntityAmmoProviderSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AlternatingEntityAmmoProviderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AlternatingEntityAmmoProviderComponent, TakeAmmoEvent>(OnTakeAmmo);
        SubscribeLocalEvent<AlternatingEntityAmmoProviderComponent, GetAmmoCountEvent>(OnGetAmmoCount);
    }

    private void OnMapInit(EntityUid uid, AlternatingEntityAmmoProviderComponent component, MapInitEvent args)
    {
        if (component.Count is null && component.Capacity is { } capacity)
            component.Count = capacity;

        Dirty(uid, component);
    }

    private void OnGetAmmoCount(EntityUid uid, AlternatingEntityAmmoProviderComponent component, ref GetAmmoCountEvent args)
    {
        args.Capacity = component.Capacity ?? int.MaxValue;
        args.Count = component.Count ?? int.MaxValue;
    }

    private void OnTakeAmmo(EntityUid uid, AlternatingEntityAmmoProviderComponent component, TakeAmmoEvent args)
    {
        for (var i = 0; i < args.Shots; i++)
        {
            if (component.Count is <= 0)
                return;

            if (component.Count is { } count)
                component.Count = count - 1;

            var proto = component.UseB ? component.ProtoB : component.ProtoA;
            var ammo = Spawn(proto, args.Coordinates);
            args.Ammo.Add((ammo, EnsureShootable(ammo)));

            if (TryComp<GunComponent>(uid, out var gun))
            {
                var sound = component.UseB ? component.SoundB : component.SoundA;
                if (sound != null)
                {
                    gun.SoundGunshotModified = sound;
                    Dirty(uid, gun);
                }
            }

            component.UseB = !component.UseB;
        }

        Dirty(uid, component);
    }

    private IShootable EnsureShootable(EntityUid uid)
    {
        if (TryComp<CartridgeAmmoComponent>(uid, out var cartridge))
            return cartridge;

        return EnsureComp<AmmoComponent>(uid);
    }
}
