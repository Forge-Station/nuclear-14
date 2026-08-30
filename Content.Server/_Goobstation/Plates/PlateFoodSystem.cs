// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.Plates;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Nutrition.Components;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Goobstation.Server.Plates;

/// <summary>
/// Allows eating food directly off a plate: via an "Eat" context menu verb,
/// by activating the plate, or by using a utensil on it.
/// </summary>
public sealed class PlateFoodSystem : EntitySystem
{
    [Dependency] private readonly FoodSystem _food = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FoodPlateComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<FoodPlateComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<FoodPlateComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FoodPlateComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnUseInHand(Entity<FoodPlateComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryEatOff(ent.Owner, args.User);
    }

    private void OnActivateInWorld(Entity<FoodPlateComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = TryEatOff(ent.Owner, args.User);
    }

    private void OnInteractUsing(Entity<FoodPlateComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<UtensilComponent>(args.Used))
            return;

        args.Handled = TryEatOff(ent.Owner, args.User);
    }

    private void OnGetVerbs(Entity<FoodPlateComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (!TryGetFood(ent.Owner, out var food, out var foodComp))
            return;

        if (!_food.IsDigestibleBy(args.User, food, foodComp))
            return;

        var user = args.User;
        var plate = ent.Owner;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () =>
            {
                // Re-resolve the current food when the verb fires; it may have changed
                // or been removed while the menu was open.
                TryEatOff(plate, user);
            },
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/cutlery.svg.192dpi.png")),
            Text = Loc.GetString("food-system-verb-eat"),
            Priority = -1
        });
    }

    private bool TryGetFood(EntityUid plate, out EntityUid food, out FoodComponent foodComp)
    {
        food = default;
        foodComp = default!;

        return _itemSlots.TryGetSlot(plate, "food_slot", out var slot)
            && slot.Item != null
            && TryComp(slot.Item.Value, out foodComp!)
            && (food = slot.Item.Value) != default;
    }

    private bool TryEatOff(EntityUid plate, EntityUid user)
    {
        if (!TryGetFood(plate, out var food, out var foodComp))
            return false;

        return _food.TryFeed(user, user, food, foodComp).Handled;
    }
}
