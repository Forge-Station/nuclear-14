using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;

namespace Content.Shared._N14.ArmorMod;

public sealed class SharedArmorModSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorModifiableComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, ArmorModifiableComponent component, ExaminedEvent args)
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
            if (!_itemSlots.TryGetSlot(uid, slotId, out var slot))
                continue;

            if (slot.Item is not { } modItem || !TryComp<ArmorModComponent>(modItem, out _))
            {
                args.PushMarkup(Loc.GetString("armor-mod-slot-empty",
                    ("slot", Loc.GetString(slot.Name))));
            }
            else
            {
                args.PushMarkup(Loc.GetString("armor-mod-slot-filled",
                    ("slot", Loc.GetString(slot.Name)),
                    ("mod", Name(modItem))));
            }
        }
    }
}
