using Content.Shared._N14.ArmorMod;
using Content.Shared.Armor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Shared.Containers;

namespace Content.Server._N14.ArmorMod;

public sealed class ArmorModSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorModifiableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ArmorModifiableComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ArmorModifiableComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ArmorModifiableComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnMapInit(EntityUid uid, ArmorModifiableComponent component, MapInitEvent args)
    {
        if (!TryComp<ArmorComponent>(uid, out var armor))
            return;

        // Save the unmodified YAML values as the base.
        // Must happen before any mods are applied.
        component.BaseModifiers = CopyModifiers(armor.Modifiers);
    }

    private void OnContainerModified(EntityUid uid, ArmorModifiableComponent component, ContainerModifiedMessage args)
    {
        if (!component.ModSlots.Contains(args.Container.ID))
            return;

        // Guard: if BaseModifiers was never populated (e.g. entity loaded from
        // a save before this component existed), re-read it from ArmorComponent
        // minus any currently-installed mods by just snapshotting current state.
        // In normal gameplay MapInit always runs first, so this is a safety net only.
        if (!TryComp<ArmorComponent>(uid, out var armor))
            return;

        if (component.BaseModifiers.Coefficients.Count == 0 &&
            component.BaseModifiers.FlatReduction.Count == 0 &&
            (armor.Modifiers.Coefficients.Count > 0 || armor.Modifiers.FlatReduction.Count > 0))
        {
            component.BaseModifiers = CopyModifiers(armor.Modifiers);
        }

        RefreshModifiers(uid, component, armor);
    }

    private void OnInteractUsing(EntityUid uid, ArmorModifiableComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_tool.HasQuality(args.Used, component.LockToolQuality))
            return;

        args.Handled = true;

        component.SlotsLocked = !component.SlotsLocked;
        Dirty(uid, component);

        foreach (var slotId in component.ModSlots)
            _itemSlots.SetLock(uid, slotId, component.SlotsLocked);

        var msg = component.SlotsLocked
            ? Loc.GetString("armor-mod-slots-locked")
            : Loc.GetString("armor-mod-slots-unlocked");

        _popup.PopupEntity(msg, uid, args.User);
    }

    public void RefreshModifiers(EntityUid uid, ArmorModifiableComponent? modifiable = null, ArmorComponent? armor = null)
    {
        if (!Resolve(uid, ref modifiable, ref armor))
            return;

        var result = CopyModifiers(modifiable.BaseModifiers);

        foreach (var slotId in modifiable.ModSlots)
        {
            // GetItemOrNull is simpler and avoids accessing slot.ContainerSlot directly
            var modItem = _itemSlots.GetItemOrNull(uid, slotId);
            if (modItem == null)
                continue;

            if (!TryComp<ArmorModComponent>(modItem.Value, out var mod))
                continue;

            ApplyModifiers(result, mod.Modifiers);
        }

        armor.Modifiers = result;
        Dirty(uid, armor);
    }

    private static void ApplyModifiers(DamageModifierSet target, DamageModifierSet mod)
    {
        foreach (var (type, modCoeff) in mod.Coefficients)
        {
            target.Coefficients[type] = target.Coefficients.TryGetValue(type, out var baseCoeff)
                ? baseCoeff * modCoeff
                : modCoeff;
        }

        foreach (var (type, modFlat) in mod.FlatReduction)
        {
            target.FlatReduction[type] = target.FlatReduction.TryGetValue(type, out var baseFlat)
                ? baseFlat + modFlat
                : modFlat;
        }
    }

    private static DamageModifierSet CopyModifiers(DamageModifierSet source)
    {
        return new DamageModifierSet
        {
            Coefficients = new Dictionary<string, float>(source.Coefficients),
            FlatReduction = new Dictionary<string, float>(source.FlatReduction),
        };
    }
}
