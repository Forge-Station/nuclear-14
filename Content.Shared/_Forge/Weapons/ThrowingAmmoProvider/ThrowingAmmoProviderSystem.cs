using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Whitelist;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Shared.Weapons.Ranged.Systems;

public sealed class ThrowingAmmoProviderSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedItemSystem _itemSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ThrowingAmmoProviderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, TakeAmmoEvent>(OnTakeAmmo);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, GetAmmoCountEvent>(OnGetAmmoCount);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, ItemUnwieldedEvent>(OnUnwielded);

        SubscribeLocalEvent<CannonBoostedComponent, GetThrowingDamageEvent>(OnGetThrowingDamage);
        SubscribeLocalEvent<CannonBoostedComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<CannonBoostedComponent, ThrowDoHitEvent>(OnThrowDoHit);
        SubscribeLocalEvent<CannonBoostedComponent, StopThrowEvent>(OnStopThrow);
    }

    // -- ThrowingAmmoProvider --

    private void OnMapInit(EntityUid uid, ThrowingAmmoProviderComponent comp, MapInitEvent args)
    {
        EnsureSlots(uid, comp);
        SyncState(uid, comp);
    }

    private void EnsureSlots(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        EnsureComp<ItemSlotsComponent>(uid);

        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);
            if (_itemSlots.TryGetSlot(uid, slotId, out _))
                continue;

            var slot = new ItemSlot(comp.SlotTemplate)
            {
                Name = string.Empty,
                Whitelist = comp.Whitelist,
            };
            _itemSlots.AddItemSlot(uid, slotId, slot);
        }
    }

    private void OnGetAmmoCount(EntityUid uid, ThrowingAmmoProviderComponent comp, ref GetAmmoCountEvent args)
    {
        args.Count = comp.AmmoCount;
        args.Capacity = comp.Capacity;
    }

    private void OnTakeAmmo(EntityUid uid, ThrowingAmmoProviderComponent comp, TakeAmmoEvent args)
    {
        for (var shot = 0; shot < args.Shots; shot++)
        {
            ItemSlot? slot = null;
            for (var i = comp.Capacity - 1; i >= 0; i--)
            {
                var slotId = GetSlotId(comp, i);
                if (_itemSlots.TryGetSlot(uid, slotId, out var s) && s.Item != null)
                {
                    slot = s;
                    break;
                }
            }

            if (slot == null || slot.Locked || slot.ContainerSlot?.ContainedEntity == null)
                break;

            var ent = slot.ContainerSlot.ContainedEntity.Value;

            if (!_containers.Remove(ent, slot.ContainerSlot))
                continue;

            EnsureComp<HiddenUntilThrownComponent>(ent);
            ApplyCannonBoost(ent, comp);

            var ammo = EnsureComp<AmmoComponent>(ent);
            args.Ammo.Add((ent, ammo));
        }
    }

    private void OnInteractUsing(EntityUid uid, ThrowingAmmoProviderComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<DamageOtherOnHitComponent>(args.Used))
            return;

        if (comp.Whitelist != null && !_whitelist.IsValid(comp.Whitelist, args.Used))
            return;

        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);

            if (!_itemSlots.TryGetSlot(uid, slotId, out var slot) || slot.Item != null)
                continue;

            if (_itemSlots.TryInsert(uid, slotId, args.Used, args.User, excludeUserAudio: true))
            {
                _audio.PlayPredicted(comp.SoundInsert, uid, args.User);
                args.Handled = true;
                return;
            }
        }
    }

    private void OnAttemptShoot(EntityUid uid, ThrowingAmmoProviderComponent comp, ref AttemptShootEvent args)
    {
        args.ThrowItems = true;
    }

    private void OnAmmoShot(EntityUid uid, ThrowingAmmoProviderComponent comp, ref AmmoShotEvent args)
    {
        if (TryComp<GunComponent>(uid, out var gun) && gun.SoundGunshot != null)
            _audio.PlayPredicted(gun.SoundGunshot, uid, null);
    }

    private void OnSlotChanged(EntityUid uid, ThrowingAmmoProviderComponent comp, ContainerModifiedMessage args)
    {
        if (!IsOurSlot(uid, comp, args.Container))
            return;

        SyncState(uid, comp);
    }

    private void OnWielded(EntityUid uid, ThrowingAmmoProviderComponent comp, ref ItemWieldedEvent args)
    {
        UpdateHeldPrefix(uid, comp);
    }

    private void OnUnwielded(EntityUid uid, ThrowingAmmoProviderComponent comp, ref ItemUnwieldedEvent args)
    {
        UpdateHeldPrefix(uid, comp);
    }

    // -- CannonBoosted --

    private void OnGetThrowingDamage(EntityUid uid, CannonBoostedComponent comp, ref GetThrowingDamageEvent args)
    {
        args.Damage *= comp.DamageMultiplier;
    }

    private void OnThrown(EntityUid uid, CannonBoostedComponent comp, ThrownEvent args)
    {
        RemCompDeferred<HiddenUntilThrownComponent>(uid);

        if (comp.MinimumFlyTime <= 0f)
            return;

        if (!TryComp<ThrownItemComponent>(uid, out var thrown) || thrown.ThrownTime == null)
            return;

        var minTime = TimeSpan.FromSeconds(comp.MinimumFlyTime);
        var thrownTime = thrown.ThrownTime.Value;
        var landTime = thrown.LandTime ?? thrownTime;

        if (landTime - thrownTime >= minTime)
            return;

        thrown.LandTime = thrownTime + minTime;
        Dirty(uid, thrown);
    }

    private void OnThrowDoHit(EntityUid uid, CannonBoostedComponent comp, ThrowDoHitEvent args)
    {
        RemCompDeferred<HiddenUntilThrownComponent>(uid);
        RemCompDeferred<CannonBoostedComponent>(uid);
    }

    private void OnStopThrow(EntityUid uid, CannonBoostedComponent comp, StopThrowEvent args)
    {
        RemCompDeferred<HiddenUntilThrownComponent>(uid);
        RemCompDeferred<CannonBoostedComponent>(uid);
    }

    // -- Helpers --

    private void ApplyCannonBoost(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        var boosted = EnsureComp<CannonBoostedComponent>(uid);
        boosted.DamageMultiplier = comp.DamageMultiplier;
        boosted.MinimumFlyTime = comp.MinimumThrowFlyTime;
        Dirty(uid, boosted);
    }

    private void SyncState(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        var count = 0;
        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);
            if (_itemSlots.TryGetSlot(uid, slotId, out var slot) && slot.Item != null)
                count++;
        }

        comp.AmmoCount = count;
        Dirty(uid, comp);

        UpdateAppearance(uid, comp);

        if (HasComp<ItemComponent>(uid))
            UpdateHeldPrefix(uid, comp);
    }

    private void UpdateAppearance(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, AmmoVisuals.AmmoCount, comp.AmmoCount, appearance);
        _appearance.SetData(uid, AmmoVisuals.AmmoMax, comp.Capacity, appearance);
        _appearance.SetData(uid, AmmoVisuals.HasAmmo, comp.AmmoCount > 0, appearance);
    }

    private void UpdateHeldPrefix(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        var isWielded = TryComp<WieldableComponent>(uid, out var wieldable) && wieldable.Wielded;

        string? prefix = null;
        if (isWielded)
            prefix = comp.AmmoCount > 0 ? "wielded-loaded" : "wielded";

        _itemSystem.SetHeldPrefix(uid, prefix, true);
    }

    private bool IsOurSlot(EntityUid uid, ThrowingAmmoProviderComponent comp, BaseContainer container)
    {
        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);
            if (_itemSlots.TryGetSlot(uid, slotId, out var slot) && slot.ContainerSlot == container)
                return true;
        }
        return false;
    }

    private static string GetSlotId(ThrowingAmmoProviderComponent comp, int index) => $"{comp.ContainerId}_{index}";
}
