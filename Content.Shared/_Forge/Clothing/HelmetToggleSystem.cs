using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Shared._N14.Clothing;

public sealed class HelmetToggleSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HelmetToggleComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<HelmetToggleComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<HelmetToggleComponent, ToggleHelmetEvent>(OnToggle);
    }

    private void OnGetActions(EntityUid uid, HelmetToggleComponent comp, GetItemActionsEvent args)
    {
        if (args.SlotFlags == SlotFlags.HEAD)
            args.AddAction(ref comp.ToggleActionEntity, comp.ToggleAction);
    }

    private void OnUnequipped(EntityUid uid, HelmetToggleComponent comp, GotUnequippedEvent args)
    {
        if (!comp.IsToggled)
            return;

        comp.IsToggled = false;
        Dirty(uid, comp);
        RaiseToggleEvent(uid, comp, args.Equipee);
    }

    private void OnToggle(EntityUid uid, HelmetToggleComponent comp, ToggleHelmetEvent args)
    {
        comp.IsToggled = !comp.IsToggled;
        Dirty(uid, comp);

        if (comp.ToggleActionEntity != null)
            _actions.SetToggled(comp.ToggleActionEntity.Value, comp.IsToggled);

        RaiseToggleEvent(uid, comp, args.Performer);
    }

    private void RaiseToggleEvent(EntityUid uid, HelmetToggleComponent comp, EntityUid wearer)
    {
        // ClothingSystem уже слушает ItemMaskToggledEvent и вызывает SetEquippedPrefix.
        // Когда IsToggled = true → prefix = AltPrefix → ищет стейт "{AltPrefix}-equipped-HELMET".
        // Когда IsToggled = false → prefix = null → ищет стандартный equipped-HELMET.
        var ev = new ItemMaskToggledEvent(wearer, comp.AltPrefix, comp.IsToggled, false);
        RaiseLocalEvent(uid, ref ev);
    }
}

/// <summary>Event raised when the helmet toggle action is used.</summary>
public sealed partial class ToggleHelmetEvent : InstantActionEvent { }

