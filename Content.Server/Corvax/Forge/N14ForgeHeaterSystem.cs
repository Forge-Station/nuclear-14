using System.Numerics;
using Content.Server.Popups;
using Content.Shared.Corvax.Forge;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Timing;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Corvax.Forge;

/// <summary>
///     Handles the furnace (печь) and the gorn (горн).
///     Both are slot-based heaters: put a fitting item inside, wait for the timer,
///     then take the result with an empty hand.
/// </summary>
public sealed class N14ForgeHeaterSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14ForgeFurnaceComponent, ComponentInit>(OnFurnaceInit);
        SubscribeLocalEvent<N14ForgeGornComponent, ComponentInit>(OnGornInit);
        SubscribeLocalEvent<N14ForgeFurnaceComponent, InteractUsingEvent>(OnFurnaceInteractUsing);
        SubscribeLocalEvent<N14ForgeGornComponent, InteractUsingEvent>(OnGornInteractUsing);
        SubscribeLocalEvent<N14ForgeFurnaceComponent, InteractHandEvent>(OnFurnaceInteractHand);
        SubscribeLocalEvent<N14ForgeGornComponent, InteractHandEvent>(OnGornInteractHand);
        SubscribeLocalEvent<N14ForgeFurnaceComponent, ExaminedEvent>(OnFurnaceExamined);
        SubscribeLocalEvent<N14ForgeGornComponent, ExaminedEvent>(OnGornExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<N14ForgeFurnaceComponent>();
        while (query.MoveNext(out var uid, out var furnace))
        {
            if (!furnace.Cooking || furnace.EndTime is not { } end)
                continue;

            if (_timing.CurTime < end)
            {
                // The coal burns off gradually while the furnace works.
                BurnCoal(uid, furnace.FuelSlot, furnace.CoalCost, Progress(end, furnace.CookTime), ref furnace.FuelUsedThisCook);
                continue;
            }

            FinishCookFurnace(uid, furnace);
        }

        var gornQuery = EntityQueryEnumerator<N14ForgeGornComponent>();
        while (gornQuery.MoveNext(out var uid, out var gorn))
        {
            if (!gorn.Heating || gorn.EndTime is not { } heatEnd)
                continue;

            if (_timing.CurTime < heatEnd)
            {
                // The coal burns off gradually while the gorn heats the plate.
                BurnCoal(uid, gorn.FuelSlot, gorn.CoalCost, Progress(heatEnd, gorn.HeatTime), ref gorn.FuelUsedThisCook);
                continue;
            }

            FinishHeatGorn(uid, gorn);
        }
    }

    private void OnFurnaceInit(EntityUid uid, N14ForgeFurnaceComponent component, ComponentInit args)
    {
        component.Slot = _container.EnsureContainer<ContainerSlot>(uid, "n14-forge-furnace");
        component.FuelSlot = _container.EnsureContainer<ContainerSlot>(uid, "n14-forge-furnace-fuel");
    }

    private void OnGornInit(EntityUid uid, N14ForgeGornComponent component, ComponentInit args)
    {
        component.Slot = _container.EnsureContainer<ContainerSlot>(uid, "n14-forge-gorn");
        component.FuelSlot = _container.EnsureContainer<ContainerSlot>(uid, "n14-forge-gorn-fuel");
    }

    private void OnFurnaceInteractUsing(EntityUid uid, N14ForgeFurnaceComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Load coal into the fuel bunker.
        if (IsCoal(args.Used))
        {
            TryInsertCoal(uid, component.FuelSlot, component.MaxCoal, args);
            return;
        }

        // Determine what the furnace cooks into.
        string? resultProto = null;
        if (TryComp<N14ForgeBloomComponent>(args.Used, out var bloom) && bloom.Clean)
        {
            // A clean bloom is cooked into a flat plate (тарелка) of its armor set.
            resultProto = bloom.Set switch
            {
                N14ForgeArmorSet.Kratos => "N14ForgePlateV2",
                N14ForgeArmorSet.Bull => "N14ForgePlateV3",
                _ => "N14ForgePlate",
            };
        }
        else if (TryComp<N14ForgePlateComponent>(args.Used, out _))
        {
            // A welded plate (сваренная) is cooked into its forged (кованая) counterpart.
            resultProto = GetForgedProto(args.Used);
        }

        if (resultProto == null)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-furnace-wrong-item"), uid, args.User);
            return;
        }

        // A melt needs fuel. Refuse to start until there is enough coal.
        if (!RequireCoal(uid, component.FuelSlot, component.CoalCost, args.User))
            return;

        if (!_container.Insert(args.Used, component.Slot))
            return;

        component.ResultProto = resultProto;
        component.Cooking = true;
        component.EndTime = _timing.CurTime + TimeSpan.FromSeconds(component.CookTime);
        component.FuelUsedThisCook = 0;
        Dirty(uid, component);

        _appearance.SetData(uid, N14ForgeFurnaceVisuals.Cooking, CookingVisualState.Cooking);

        args.Handled = true;
    }

    private void OnFurnaceInteractHand(EntityUid uid, N14ForgeFurnaceComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        // You cannot take the bloom/plate out while it is still cooking.
        if (component.Cooking)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-furnace-block-remove"), uid, args.User);
            args.Handled = true;
            return;
        }

        GrabOut(uid, component, component.Slot, args.User);
        args.Handled = true;
    }

    private void OnFurnaceExamined(EntityUid uid, N14ForgeFurnaceComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var fuel = GetFuelAmount(component.FuelSlot);
        if (fuel > 0)
            args.PushMarkup(Loc.GetString("n14-forge-fuel-amount", ("amount", fuel)));

        if (!component.Cooking || component.EndTime == null)
            return;

        var remaining = component.EndTime.Value - _timing.CurTime;
        if (remaining <= TimeSpan.Zero)
            return;

        args.PushMarkup(Loc.GetString("n14-forge-furnace-cooking-time", ("time", Math.Ceiling(remaining.TotalSeconds))));
    }

    private void OnGornInteractUsing(EntityUid uid, N14ForgeGornComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Load coal into the fuel bunker.
        if (IsCoal(args.Used))
        {
            TryInsertCoal(uid, component.FuelSlot, component.MaxCoal, args);
            return;
        }

        // Only plates/halves can be heated in the gorn.
        if (!HasComp<N14ForgePlateComponent>(args.Used))
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-gorn-wrong-item"), uid, args.User);
            return;
        }

        // Heating needs fuel. Refuse to start until there is enough coal.
        if (!RequireCoal(uid, component.FuelSlot, component.CoalCost, args.User))
            return;

        if (!_container.Insert(args.Used, component.Slot))
            return;

        component.Heating = true;
        component.EndTime = _timing.CurTime + TimeSpan.FromSeconds(component.HeatTime);
        component.FuelUsedThisCook = 0;
        Dirty(uid, component);

        _appearance.SetData(uid, N14ForgeFurnaceVisuals.Cooking, CookingVisualState.Cooking);

        args.Handled = true;
    }

    private void OnGornInteractHand(EntityUid uid, N14ForgeGornComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        GrabOut(uid, component, component.Slot, args.User);
        args.Handled = true;
    }

    private void OnGornExamined(EntityUid uid, N14ForgeGornComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var fuel = GetFuelAmount(component.FuelSlot);
        if (fuel > 0)
            args.PushMarkup(Loc.GetString("n14-forge-fuel-amount", ("amount", fuel)));

        if (!component.Heating || component.EndTime == null)
            return;

        var remaining = component.EndTime.Value - _timing.CurTime;
        if (remaining <= TimeSpan.Zero)
            return;

        args.PushMarkup(Loc.GetString("n14-forge-gorn-heating-time", ("time", Math.Ceiling(remaining.TotalSeconds))));
    }

    private void FinishCookFurnace(EntityUid uid, N14ForgeFurnaceComponent component)
    {
        component.Cooking = false;
        component.EndTime = null;
        component.FuelUsedThisCook = 0;
        Dirty(uid, component);

        var contained = component.Slot.ContainedEntity;
        if (contained != null)
        {
            _container.Remove(contained.Value, component.Slot);
            Del(contained.Value);
        }

        // The result (a plate or a forged plate) is spawned right into the slot.
        var result = Spawn(component.ResultProto, Transform(uid).Coordinates);
        _container.Insert(result, component.Slot);

        StopHeaterVisuals(uid);
    }

    private void FinishHeatGorn(EntityUid uid, N14ForgeGornComponent component)
    {
        component.Heating = false;
        component.EndTime = null;
        component.FuelUsedThisCook = 0;
        Dirty(uid, component);

        if (component.Slot.ContainedEntity is { } contained)
            EnsureComp<N14ForgeHeatedComponent>(contained);

        StopHeaterVisuals(uid);
    }

    private void StopHeaterVisuals(EntityUid uid)
    {
        _appearance.SetData(uid, N14ForgeFurnaceVisuals.Cooking, CookingVisualState.Idle);
    }

    /// <summary>
    ///     Fraction of the total duration already elapsed, in [0; 1].
    /// </summary>
    private float Progress(TimeSpan end, float duration)
    {
        var remaining = (end - _timing.CurTime).TotalSeconds;
        return 1f - (float)(remaining / Math.Max(duration, 0.01f));
    }

    /// <summary>
    ///     Burns coal off the fuel bunker gradually, one unit at a time,
    ///     so that the whole <see cref="N14ForgeFurnaceComponent.CoalCost"/> is gone exactly when the cook/heat ends.
    /// </summary>
    private void BurnCoal(EntityUid uid, ContainerSlot fuelSlot, int coalCost, float progress, ref int used)
    {
        var target = Math.Clamp((int)MathF.Floor(coalCost * Math.Clamp(progress, 0f, 1f)), 0, coalCost);
        var toBurn = target - used;
        if (toBurn <= 0)
            return;

        if (fuelSlot.ContainedEntity is { } fuel && TryComp<StackComponent>(fuel, out var stack))
            _stack.Use(fuel, Math.Min(toBurn, stack.Count));

        used += toBurn;
    }

    /// <summary>
    ///     Whether the used item is coal (Coal stack type, e.g. Coal1).
    /// </summary>
    private bool IsCoal(EntityUid uid)
        => TryComp<StackComponent>(uid, out var stack) && stack.StackTypeId == "Coal";

    /// <summary>
    ///     Amount of coal units currently inside the fuel bunker.
    /// </summary>
    private int GetFuelAmount(ContainerSlot slot)
        => slot.ContainedEntity is { } fuel && TryComp<StackComponent>(fuel, out var stack) ? stack.Count : 0;

    /// <summary>
    ///     Returns false and shows a popup when the bunker has less coal than needed.
    /// </summary>
    private bool RequireCoal(EntityUid uid, ContainerSlot fuelSlot, int coalCost, EntityUid user)
    {
        var current = GetFuelAmount(fuelSlot);
        if (current >= coalCost)
            return true;

        _popup.PopupEntity(Loc.GetString("n14-forge-need-coal", ("amount", coalCost), ("current", current)), uid, user);
        return false;
    }

    /// <summary>
    ///     Puts the held coal stack into the fuel bunker (only if the bunker is empty and big enough).
    /// </summary>
    private void TryInsertCoal(EntityUid uid, ContainerSlot fuelSlot, int maxCoal, InteractUsingEvent args)
    {
        if (!TryComp<StackComponent>(args.Used, out var stack))
            return;

        if (fuelSlot.ContainedEntity != null || stack.Count > maxCoal)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-fuel-full"), uid, args.User);
            args.Handled = true;
            return;
        }

        _container.Insert(args.Used, fuelSlot);
        args.Handled = true;
    }

    /// <summary>
    ///     Maps a welded plate prototype to its forged (кованый) counterpart.
    /// </summary>
    private string? GetForgedProto(EntityUid uid)
    {
        if (!TryComp<N14ForgePlateComponent>(uid, out var plate) || plate.Variant != N14ForgePlateVariant.Welded)
            return null;

        var setSuffix = plate.Set switch
        {
            N14ForgeArmorSet.Kratos => "V2",
            N14ForgeArmorSet.Bull => "V3",
            _ => "",
        };

        switch (plate.PlateType)
        {
            case N14ForgePlateType.TorsoPlate:
                return $"N14ForgeForgedTorsoplate{setSuffix}";
            case N14ForgePlateType.ArmPlate:
                return $"N14ForgeForgedArmPlate{setSuffix}";
            case N14ForgePlateType.LegPlate:
                return $"N14ForgeForgedLegPlate{setSuffix}";
            default:
                return null;
        }
    }

    /// <summary>
    ///     Ejects the container content into the user's hands (or drops it).
    /// </summary>
    private void GrabOut(EntityUid uid, Component component, ContainerSlot slot, EntityUid user)
    {
        if (slot.ContainedEntity is not { } contained)
            return;

        _container.Remove(contained, slot);
        _hands.TryPickup(user, contained, checkActionBlocker: false);
    }
}