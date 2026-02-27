using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Robust.Shared.Containers;

namespace Content.Shared._N14.ArmorMod;

public sealed class SharedArmorModSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorModifiableComponent, ExaminedEvent>(OnArmorExamined);
        SubscribeLocalEvent<ArmorModComponent, ExaminedEvent>(OnModExamined);
    }

    private void OnArmorExamined(EntityUid uid, ArmorModifiableComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var lockLine = component.SlotsLocked
            ? Loc.GetString("armor-mod-examine-locked")
            : Loc.GetString("armor-mod-examine-unlocked");

        args.PushMarkup(lockLine);
        args.PushMarkup(Loc.GetString("armor-mod-examine-header"));

        foreach (var slotId in component.ModSlots)
        {
            // Use ContainerSystem directly — ContainerSlot on ItemSlot is [NonSerialized]
            // and null on the client, so we cannot use _itemSlots.GetItemOrNull here.
            _container.TryGetContainer(uid, slotId, out var container);
            var modItem = container?.ContainedEntities.Count > 0
                ? container.ContainedEntities[0]
                : (EntityUid?) null;

            // Get the human-readable slot name (slot.Name is networked).
            var slotName = slotId;
            if (_itemSlots.TryGetSlot(uid, slotId, out var slot))
                slotName = Loc.GetString(slot.Name);

            if (modItem == null || !TryComp<ArmorModComponent>(modItem.Value, out _))
            {
                args.PushMarkup(Loc.GetString("armor-mod-slot-empty", ("slot", slotName)));
            }
            else
            {
                args.PushMarkup(Loc.GetString("armor-mod-slot-filled",
                    ("slot", slotName),
                    ("mod", Name(modItem.Value))));
            }
        }
    }

    private void OnModExamined(EntityUid uid, ArmorModComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var mods = component.Modifiers;
        if (mods.Coefficients.Count == 0 && mods.FlatReduction.Count == 0)
            return;

        args.PushMarkup(Loc.GetString("armor-mod-stat-header"));

        foreach (var (type, coeff) in mods.Coefficients)
        {
            var armorType = Loc.GetString("armor-damage-type-" + type.ToLower());
            args.PushMarkup(Loc.GetString("armor-coefficient-value",
                ("type", armorType),
                ("value", MathF.Round((1f - coeff) * 100, 1))));
        }

        foreach (var (type, flat) in mods.FlatReduction)
        {
            var armorType = Loc.GetString("armor-damage-type-" + type.ToLower());
            args.PushMarkup(Loc.GetString("armor-reduction-value",
                ("type", armorType),
                ("value", flat)));
        }
    }
}
