using Content.Shared.Corvax.Forge;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.Containers;

namespace Content.Server.Corvax.Forge;

/// <summary>
///     Handles the automatic power armor gauntlets of the forged Legion armor.
///     Each armor owns a single persistent gauntlet entity stored in its internal container.
///     Wearing the armor moves the gauntlets into the gloves slot (dropping any previously
///     worn gloves); removing the armor stashes them back. The gauntlets cannot be removed
///     manually while the armor exists.
/// </summary>
public sealed class N14ForgeArmorSystem : EntitySystem
{
    private const string GauntletsProto = "N14ClothingPAGauntlets";
    private const string GlovesSlot = "gloves";

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14ForgePowerArmorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<N14ForgePowerArmorComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<N14ForgePowerArmorComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<N14ForgePowerArmorComponent, ComponentRemove>(OnRemoved);
        SubscribeLocalEvent<N14ForgeGauntletComponent, BeingUnequippedAttemptEvent>(OnGauntletUnequipAttempt);
    }

    private void OnMapInit(EntityUid uid, N14ForgePowerArmorComponent component, MapInitEvent args)
    {
        EnsureGauntlets(uid, component);
    }

    private void OnEquipped(EntityUid uid, N14ForgePowerArmorComponent component, GotEquippedEvent args)
    {
        // Only act when the armor itself is worn as the outer clothing.
        if ((args.SlotFlags & SlotFlags.OUTERCLOTHING) == 0)
            return;

        EnsureGauntlets(uid, component);

        if (component.Gauntlets is not { } gauntlet || !Exists(gauntlet))
            return;

        var wearer = args.Equipee;

        // The gloves slot already holds our gauntlets or foreign power armor gauntlets - nothing to do.
        if (_inventory.TryGetSlotEntity(wearer, GlovesSlot, out var current))
        {
            if (current.Value == gauntlet)
                return;

            if (MetaData(current.Value).EntityPrototype?.ID == GauntletsProto)
                return;
        }

        // Throw the previously worn gloves on the ground.
        if (current != null)
            _inventory.TryUnequip(wearer, GlovesSlot, force: true, silent: true);

        // Move our gauntlets out of the armor into the gloves slot.
        _inventory.TryEquip(wearer, gauntlet, GlovesSlot, force: true);
    }

    private void OnUnequipped(EntityUid uid, N14ForgePowerArmorComponent component, GotUnequippedEvent args)
    {
        if ((args.SlotFlags & SlotFlags.OUTERCLOTHING) == 0)
            return;

        if (component.Gauntlets is not { } gauntlet || !Exists(gauntlet))
            return;

        // If our gauntlets are still worn, stash them back into the armor instead of dropping them.
        if (component.Container is { } container
            && _inventory.TryGetSlotEntity(args.Equipee, GlovesSlot, out var current)
            && current.Value == gauntlet)
        {
            _container.Insert(gauntlet, container, force: true);
        }
    }

    private void OnRemoved(EntityUid uid, N14ForgePowerArmorComponent component, ComponentRemove args)
    {
        if (component.Gauntlets is { } gauntlet)
            QueueDel(gauntlet);
    }

    private void OnGauntletUnequipAttempt(EntityUid uid, N14ForgeGauntletComponent component, BeingUnequippedAttemptEvent args)
    {
        if (Exists(component.Owner))
            args.Cancel();
    }

    private void EnsureGauntlets(EntityUid uid, N14ForgePowerArmorComponent component)
    {
        component.Container ??= _container.EnsureContainer<ContainerSlot>(
            uid,
            N14ForgePowerArmorComponent.DefaultGauntletContainerId);

        // We already know about our gauntlets - just make sure they are stashed.
        if (component.Gauntlets is { } existing && Exists(existing))
        {
            if (!component.Container.Contains(existing))
                _container.Insert(existing, component.Container, force: true);
            return;
        }

        // A saved map may have restored the gauntlets inside the container while the
        // runtime field was lost - adopt them back.
        if (component.Container.ContainedEntity is { } contained && HasComp<N14ForgeGauntletComponent>(contained))
        {
            component.Gauntlets = contained;
            EnsureComp<N14ForgeGauntletComponent>(contained).Owner = uid;
            return;
        }

        var gauntlet = Spawn(GauntletsProto, Transform(uid).Coordinates);
        var gauntletComp = EnsureComp<N14ForgeGauntletComponent>(gauntlet);
        gauntletComp.Owner = uid;
        component.Gauntlets = gauntlet;

        _container.Insert(gauntlet, component.Container, force: true);
    }
}