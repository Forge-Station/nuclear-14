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
        SubscribeLocalEvent<ArmorModifiableComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<ArmorModifiableComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<ArmorModifiableComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ArmorModifiableComponent, ArmorModLockDoAfterEvent>(OnLockDoAfter);
    }

    private void OnMapInit(EntityUid uid, ArmorModifiableComponent component, MapInitEvent args)
    {
        if (!TryComp<ArmorComponent>(uid, out var armor))
            return;

        component.BaseModifiers = CopyModifiers(armor.Modifiers);
        component.BaseModifiersStored = true;
    }

    private void OnItemInserted(EntityUid uid, ArmorModifiableComponent component, EntInsertedIntoContainerMessage args)
    {
        if (!component.ModSlots.Contains(args.Container.ID))
            return;

        EnsureBaseModifiers(uid, component);
        RefreshModifiers(uid, component);
    }

    private void OnItemRemoved(EntityUid uid, ArmorModifiableComponent component, EntRemovedFromContainerMessage args)
    {
        if (!component.ModSlots.Contains(args.Container.ID))
            return;

        EnsureBaseModifiers(uid, component);
        RefreshModifiers(uid, component);
    }

    private void OnInteractUsing(EntityUid uid, ArmorModifiableComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_tool.HasQuality(args.Used, component.LockToolQuality))
            return;

        args.Handled = true;

        _tool.UseTool(args.Used, args.User, uid, component.LockDelay, component.LockToolQuality,
            new ArmorModLockDoAfterEvent());
    }

    private void OnLockDoAfter(EntityUid uid, ArmorModifiableComponent component, ArmorModLockDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        component.SlotsLocked = !component.SlotsLocked;
        Dirty(uid, component);

        foreach (var slotId in component.ModSlots)
            _itemSlots.SetLock(uid, slotId, component.SlotsLocked);

        var msg = component.SlotsLocked
            ? Loc.GetString("armor-mod-slots-locked")
            : Loc.GetString("armor-mod-slots-unlocked");

        _popup.PopupEntity(msg, uid, args.User);
    }

    /// <summary>
    /// Safety net: if BaseModifiers was never stored (e.g. entity loaded from a save
    /// before this component existed), snapshot current armor state.
    /// In normal gameplay MapInit always runs first so this is rarely needed.
    /// </summary>
    private void EnsureBaseModifiers(EntityUid uid, ArmorModifiableComponent component)
    {
        if (component.BaseModifiersStored)
            return;

        if (!TryComp<ArmorComponent>(uid, out var armor))
            return;

        component.BaseModifiers = CopyModifiers(armor.Modifiers);
        component.BaseModifiersStored = true;
    }

    public void RefreshModifiers(EntityUid uid, ArmorModifiableComponent? modifiable = null, ArmorComponent? armor = null)
    {
        if (!Resolve(uid, ref modifiable, ref armor))
            return;

        var result = CopyModifiers(modifiable.BaseModifiers);

        foreach (var slotId in modifiable.ModSlots)
        {
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
