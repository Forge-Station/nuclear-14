using System.Linq;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

// Goobstation additions
using Content.Shared.CCVar;
using Content.Shared._Goobstation.CCVar;
using Content.Shared.Emag.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Robust.Shared.Configuration;
using Content.Shared.Vehicles; // for HornActionEvent, SirenActionEvent // Forge-Change

namespace Content.Shared.Mech.EntitySystems;

/// <summary>
/// Handles all of the interactions, UI handling, and items shennanigans for <see cref="MechComponent"/>
/// </summary>
public abstract class SharedMechSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!; // Goobstation Change
    [Dependency] private readonly SharedVirtualItemSystem _virtualItem = default!; // Goobstation Change
    [Dependency] private readonly IConfigurationManager _config = default!; // Goobstation Change

    // Goobstation: Local variable for checking if mech guns can be used out of them.
    private bool _canUseMechGunOutside;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MechComponent, MechToggleEquipmentEvent>(OnToggleEquipmentAction);
        SubscribeLocalEvent<MechComponent, MechEjectPilotEvent>(OnEjectPilotEvent);
        SubscribeLocalEvent<MechComponent, UserActivateInWorldEvent>(RelayInteractionEvent);
        SubscribeLocalEvent<MechComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MechComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<MechComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
        SubscribeLocalEvent<MechComponent, DragDropTargetEvent>(OnDragDrop);
        SubscribeLocalEvent<MechComponent, CanDropTargetEvent>(OnCanDragDrop);
        SubscribeLocalEvent<MechComponent, GotEmaggedEvent>(OnEmagged);

        SubscribeLocalEvent<MechComponent, HornActionEvent>(OnHornAction); // Forge-Change
        SubscribeLocalEvent<MechComponent, SirenActionEvent>(OnSirenAction); // Forge-Change
        SubscribeLocalEvent<MechPilotComponent, GetMeleeWeaponEvent>(OnGetMeleeWeapon);
        SubscribeLocalEvent<MechPilotComponent, CanAttackFromContainerEvent>(OnCanAttackFromContainer);
        SubscribeLocalEvent<MechPilotComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<MechPilotComponent, EntGotRemovedFromContainerMessage>(OnEntGotRemovedFromContainer);
        SubscribeLocalEvent<MechEquipmentComponent, ShotAttemptedEvent>(OnShotAttempted); // Goobstation

        SubscribeLocalEvent<MechPassengerComponent, EntGotRemovedFromContainerMessage>(OnPassengerRemoved); // Forge-Change

        Subs.CVar(_config, GoobCVars.MechGunOutsideMech, value => _canUseMechGunOutside = value, true); // Goobstation
    }

    // GoobStation: Fixes scram implants or teleports locking the pilot out of being able to move.
    private void OnEntGotRemovedFromContainer(EntityUid uid, MechPilotComponent component, EntGotRemovedFromContainerMessage args)
    {
        TryEject(component.Mech, pilot: uid);
    }
    private void OnPassengerRemoved(EntityUid uid, MechPassengerComponent component, EntGotRemovedFromContainerMessage args)
    {
        if (component.Mech == default)
            return;
        TryEjectPassenger(component.Mech, uid); // Forge-Change
    }

    private void OnToggleEquipmentAction(EntityUid uid, MechComponent component, MechToggleEquipmentEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        CycleEquipment(uid);
    }

    private void OnEjectPilotEvent(EntityUid uid, MechComponent component, MechEjectPilotEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
     // Forge-Change-start

        if (TryComp<MechPilotComponent>(args.Performer, out var pilot) && pilot.Mech == uid)
        {
            TryEject(uid, component);
        }
        else if (TryComp<MechPassengerComponent>(args.Performer, out var passenger) && passenger.Mech == uid)
        {
            TryEjectPassenger(uid, args.Performer);
        }
    }
    protected virtual void OnHornAction(EntityUid uid, MechComponent component, HornActionEvent args)
    {
    }
    protected virtual void OnSirenAction(EntityUid uid, MechComponent component, SirenActionEvent args)
    {
    }
    protected virtual void OnEjectPassenger1(EntityUid uid, MechComponent component, MechEjectPassenger1Event args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (component.PilotSlot.ContainedEntity != args.Performer)
            return;
        if (component.PassengerSlot1.ContainedEntity is { } passenger)
            TryEjectPassenger(uid, passenger, component);
        else
            _popup.PopupEntity(Loc.GetString("mech-no-passenger-in-slot", ("slot", 1)), uid, args.Performer);
    }

    protected virtual void OnEjectPassenger2(EntityUid uid, MechComponent component, MechEjectPassenger2Event args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (component.PilotSlot.ContainedEntity != args.Performer)
            return;
        if (component.PassengerSlot2.ContainedEntity is { } passenger)
            TryEjectPassenger(uid, passenger, component);
        else
            _popup.PopupEntity(Loc.GetString("mech-no-passenger-in-slot", ("slot", 2)), uid, args.Performer);
    }

    protected virtual void OnEjectPassenger3(EntityUid uid, MechComponent component, MechEjectPassenger3Event args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (component.PilotSlot.ContainedEntity != args.Performer)
            return;
        if (component.PassengerSlot3.ContainedEntity is { } passenger)
            TryEjectPassenger(uid, passenger, component);
        else
            _popup.PopupEntity(Loc.GetString("mech-no-passenger-in-slot", ("slot", 3)), uid, args.Performer);
    }
 // Forge-Change-end
    private void RelayInteractionEvent(EntityUid uid, MechComponent component, UserActivateInWorldEvent args)
    {
        var pilot = component.PilotSlot.ContainedEntity;
        if (pilot == null)
            return;

        if (!_timing.IsFirstTimePredicted)
            return;

        if (component.CurrentSelectedEquipment != null)
        {
            RaiseLocalEvent(component.CurrentSelectedEquipment.Value, args);
        }
    }

    private void OnStartup(EntityUid uid, MechComponent component, ComponentStartup args)
    {
        component.PilotSlot = _container.EnsureContainer<ContainerSlot>(uid, component.PilotSlotId);
        component.EquipmentContainer = _container.EnsureContainer<Container>(uid, component.EquipmentContainerId);
        component.BatterySlot = _container.EnsureContainer<ContainerSlot>(uid, component.BatterySlotId);
        component.IgnitionSlot = _container.EnsureContainer<ContainerSlot>(uid, component.IgnitionSlotId); // Forge-Change
        component.PassengerSlot1 = _container.EnsureContainer<ContainerSlot>(uid, component.PassengerSlot1Id); // Forge-Change
        component.PassengerSlot2 = _container.EnsureContainer<ContainerSlot>(uid, component.PassengerSlot2Id); // Forge-Change
        component.PassengerSlot3 = _container.EnsureContainer<ContainerSlot>(uid, component.PassengerSlot3Id); // Forge-Change
        UpdateAppearance(uid, component);
    }

    private void OnDestruction(EntityUid uid, MechComponent component, DestructionEventArgs args)
    {
        BreakMech(uid, component);
    }

    private void OnGetAdditionalAccess(EntityUid uid, MechComponent component, ref GetAdditionalAccessEvent args)
    {
        var pilot = component.PilotSlot.ContainedEntity;
        if (pilot == null)
            return;

        args.Entities.Add(pilot.Value);
    }

    private void SetupUser(EntityUid mech, EntityUid pilot, MechComponent? component = null)
    {
        if (!Resolve(mech, ref component))
            return;

        var rider = EnsureComp<MechPilotComponent>(pilot);

        var irelay = EnsureComp<InteractionRelayComponent>(pilot);

        if (component.EngineRunning) // Forge-Change
            _mover.SetRelay(pilot, mech);
        _interaction.SetRelay(pilot, mech, irelay);
        rider.Mech = mech;
        Dirty(pilot, rider);

        if (_net.IsClient)
            return;

        _actions.AddAction(pilot, ref component.MechCycleActionEntity, component.MechCycleAction, mech);
        _actions.AddAction(pilot, ref component.MechUiActionEntity, component.MechUiAction, mech);
        _actions.AddAction(pilot, ref component.MechEjectActionEntity, component.MechEjectAction, mech);
        _actions.AddAction(pilot, ref component.ToggleActionEntity, component.ToggleAction, mech);
        _actions.AddAction(pilot, ref component.MechHornActionEntity, component.MechHornAction, mech); // Forge-Change
        _actions.AddAction(pilot, ref component.MechSirenActionEntity, component.MechSirenAction, mech); // Forge-Change
        _actions.AddAction(pilot, ref component.MechEjectPassenger1ActionEntity, component.MechEjectPassenger1Action, mech); // Forge-Change
        _actions.AddAction(pilot, ref component.MechEjectPassenger2ActionEntity, component.MechEjectPassenger2Action, mech); // Forge-Change
        _actions.AddAction(pilot, ref component.MechEjectPassenger3ActionEntity, component.MechEjectPassenger3Action, mech); // Forge-Change
    }

    private void RemoveUser(EntityUid mech, EntityUid pilot)
    {
        if (!RemComp<MechPilotComponent>(pilot))
            return;
        RemComp<RelayInputMoverComponent>(pilot);
        RemComp<InteractionRelayComponent>(pilot);

        _actions.RemoveProvidedActions(pilot, mech);
    }

    /// <summary>
    /// Destroys the mech, removing the user and ejecting all installed equipment.
    /// </summary>
    public virtual void BreakMech(EntityUid uid, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        TryEject(uid, component);
        foreach (var slot in GetPassengerSlots(component))
        {
            if (slot.ContainedEntity != null)
                TryEjectPassenger(uid, slot.ContainedEntity.Value);
        } // Forge-Change
        var equipment = new List<EntityUid>(component.EquipmentContainer.ContainedEntities);
        foreach (var ent in equipment)
        {
            RemoveEquipment(uid, ent, component, forced: true);
        }

        component.Broken = true;
        UpdateAppearance(uid, component);
    }

    /// <summary>
    /// Cycles through the currently selected equipment.
    /// </summary>
    public void CycleEquipment(EntityUid uid, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var allEquipment = component.EquipmentContainer.ContainedEntities.ToList();

        var equipmentIndex = -1;
        if (component.CurrentSelectedEquipment != null)
        {
            bool StartIndex(EntityUid u) => u == component.CurrentSelectedEquipment;
            equipmentIndex = allEquipment.FindIndex(StartIndex);
        }

        equipmentIndex++;
        component.CurrentSelectedEquipment = equipmentIndex >= allEquipment.Count
            ? null
            : allEquipment[equipmentIndex];

        var popupString = component.CurrentSelectedEquipment != null
            ? Loc.GetString("mech-equipment-select-popup", ("item", component.CurrentSelectedEquipment))
            : Loc.GetString("mech-equipment-select-none-popup");

        if (_net.IsServer)
            _popup.PopupEntity(popupString, uid);

        Dirty(uid, component);
    }

    /// <summary>
    /// Inserts an equipment item into the mech.
    /// </summary>
    public void InsertEquipment(EntityUid uid, EntityUid toInsert, MechComponent? component = null,
        MechEquipmentComponent? equipmentComponent = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!Resolve(toInsert, ref equipmentComponent))
            return;

        if (component.EquipmentContainer.ContainedEntities.Count >= component.MaxEquipmentAmount)
            return;

        if (_whitelistSystem.IsWhitelistFail(component.EquipmentWhitelist, toInsert))
            return;

        equipmentComponent.EquipmentOwner = uid;
        _container.Insert(toInsert, component.EquipmentContainer);
        var ev = new MechEquipmentInsertedEvent(uid);
        RaiseLocalEvent(toInsert, ref ev);
        UpdateUserInterface(uid, component);
    }

    /// <summary>
    /// Removes an equipment item from a mech.
    /// </summary>
    public void RemoveEquipment(EntityUid uid, EntityUid toRemove, MechComponent? component = null,
        MechEquipmentComponent? equipmentComponent = null, bool forced = false)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!Resolve(toRemove, ref equipmentComponent))
            return;

        if (!forced)
        {
            var attemptev = new AttemptRemoveMechEquipmentEvent();
            RaiseLocalEvent(toRemove, ref attemptev);
            if (attemptev.Cancelled)
                return;
        }

        var ev = new MechEquipmentRemovedEvent(uid);
        RaiseLocalEvent(toRemove, ref ev);

        if (component.CurrentSelectedEquipment == toRemove)
            CycleEquipment(uid, component);

        equipmentComponent.EquipmentOwner = null;
        _container.Remove(toRemove, component.EquipmentContainer);
        UpdateUserInterface(uid, component);
    }

    /// <summary>
    /// Attempts to change the amount of energy in the mech.
    /// </summary>
    public virtual bool TryChangeEnergy(EntityUid uid, FixedPoint2 delta, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (component.Energy + delta < 0)
            return false;

        component.Energy = FixedPoint2.Clamp(component.Energy + delta, 0, component.MaxEnergy);
        Dirty(uid, component);
        UpdateUserInterface(uid, component);
        return true;
    }

    /// <summary>
    /// Sets the integrity of the mech.
    /// </summary>
    public void SetIntegrity(EntityUid uid, FixedPoint2 value, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.Integrity = FixedPoint2.Clamp(value, 0, component.MaxIntegrity);

        if (component.Integrity <= 0)
        {
            BreakMech(uid, component);
        }
        else if (component.Broken)
        {
            component.Broken = false;
            UpdateAppearance(uid, component);
        }

        Dirty(uid, component);
        UpdateUserInterface(uid, component);
    }

    /// <summary>
    /// Checks if the pilot is present
    /// </summary>
    public bool IsEmpty(MechComponent component)
    {
        return component.PilotSlot.ContainedEntity == null;
    }

    public bool CanInsert(EntityUid uid, EntityUid toInsert, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        return IsEmpty(component) && _actionBlocker.CanMove(toInsert);
    }

    /// <summary>
    /// Updates the user interface
    /// </summary>
    public virtual void UpdateUserInterface(EntityUid uid, MechComponent? component = null)
    {
    }

    /// <summary>
    /// Attempts to insert a pilot into the mech.
    /// </summary>
    public bool TryInsert(EntityUid uid, EntityUid? toInsert, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (toInsert == null || component.PilotSlot.ContainedEntity == toInsert)
            return false;

        if (!CanInsert(uid, toInsert.Value, component))
            return false;

        SetupUser(uid, toInsert.Value);
        _container.Insert(toInsert.Value, component.PilotSlot);
        UpdateAppearance(uid, component);
        UpdateHands(toInsert.Value, uid, true); // Goobstation
        return true;
    }

    /// <summary>
    /// Attempts to eject the current pilot from the mech
    /// </summary>
    public bool TryEject(EntityUid uid, MechComponent? component = null, EntityUid? pilot = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (component.PilotSlot.ContainedEntity != null)
            pilot = component.PilotSlot.ContainedEntity.Value;

        if (pilot == null)
            return false;

        RemoveUser(uid, pilot.Value);
        _container.RemoveEntity(uid, pilot.Value);
        UpdateAppearance(uid, component);
        UpdateHands(pilot.Value, uid, false); // Goobstation
        return true;
    }
 // Forge-Change-start
    public bool CanInsertPassenger(EntityUid uid, EntityUid toInsert, MechComponent component)
    {
        if (HasComp<MechPilotComponent>(toInsert) || IsPassenger(toInsert, component))
            return false;
        if (GetFreePassengerSlot(component) == null)
            return false;
        return _actionBlocker.CanInteract(toInsert, uid);
    }
    public bool TryInsertPassenger(EntityUid uid, EntityUid toInsert, MechComponent component)
    {
        if (!CanInsertPassenger(uid, toInsert, component))
            return false;

        var slot = GetFreePassengerSlot(component)!;
        _container.Insert(toInsert, slot);

        var passenger = EnsureComp<MechPassengerComponent>(toInsert);
        passenger.Mech = uid;
        passenger.Slot = slot;
        Dirty(toInsert, passenger);

        return true;
    }
    public bool TryEjectPassenger(EntityUid uid, EntityUid passenger, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        foreach (var slot in GetPassengerSlots(component))
        {
            if (slot.ContainedEntity == passenger)
            {
                _container.RemoveEntity(uid, passenger);
                RemComp<MechPassengerComponent>(passenger);
                return true;
            }
        }
        return false;
    }
    protected ContainerSlot? GetFreePassengerSlot(MechComponent component)
    {
        if (component.PassengerSlot1.ContainedEntity == null)
            return component.PassengerSlot1;
        if (component.PassengerSlot2.ContainedEntity == null)
            return component.PassengerSlot2;
        if (component.PassengerSlot3.ContainedEntity == null)
            return component.PassengerSlot3;
        return null;
    }
    protected IEnumerable<ContainerSlot> GetPassengerSlots(MechComponent component)
    {
        yield return component.PassengerSlot1;
        yield return component.PassengerSlot2;
        yield return component.PassengerSlot3;
    }
    protected bool IsPassenger(EntityUid uid, MechComponent component)
    {
        return GetPassengerSlots(component).Any(slot => slot.ContainedEntity == uid);
    }
 // Forge-Change-end
    // Goobstation Change Start
    private void UpdateHands(EntityUid uid, EntityUid mech, bool active)
    {
        if (!TryComp<HandsComponent>(uid, out var handsComponent))
            return;

        if (active)
            BlockHands(uid, mech, handsComponent);
        else
            FreeHands(uid, mech);
    }

    private void BlockHands(EntityUid uid, EntityUid mech, HandsComponent handsComponent)
    {
        var freeHands = 0;
        foreach (var hand in _hands.EnumerateHands(uid, handsComponent))
        {
            if (hand.HeldEntity == null)
            {
                freeHands++;
                continue;
            }

            // Is this entity removable? (they might have handcuffs on)
            if (HasComp<UnremoveableComponent>(hand.HeldEntity) && hand.HeldEntity != mech)
                continue;

            _hands.DoDrop(uid, hand, true, handsComponent);
            freeHands++;
            if (freeHands == 2)
                break;
        }
        if (_virtualItem.TrySpawnVirtualItemInHand(mech, uid, out var virtItem1))
            EnsureComp<UnremoveableComponent>(virtItem1.Value);

        if (_virtualItem.TrySpawnVirtualItemInHand(mech, uid, out var virtItem2))
            EnsureComp<UnremoveableComponent>(virtItem2.Value);
    }

    private void FreeHands(EntityUid uid, EntityUid mech)
    {
        _virtualItem.DeleteInHandsMatching(uid, mech);
    }
    // Goobstation Change End
    private void OnGetMeleeWeapon(EntityUid uid, MechPilotComponent component, GetMeleeWeaponEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<MechComponent>(component.Mech, out var mech))
            return;

        var weapon = mech.CurrentSelectedEquipment ?? component.Mech;
        args.Weapon = weapon;
        args.Handled = true;
    }

    private void OnCanAttackFromContainer(EntityUid uid, MechPilotComponent component, CanAttackFromContainerEvent args)
    {
        args.CanAttack = true;
    }

    private void OnAttackAttempt(EntityUid uid, MechPilotComponent component, AttackAttemptEvent args)
    {
        if (args.Target == component.Mech)
            args.Cancel();
    }

    // Goobstation: Prevent guns being used out of mechs if CCVAR is set.
    private void OnShotAttempted(EntityUid uid, MechEquipmentComponent component, ref ShotAttemptedEvent args)
    {
        if (!component.EquipmentOwner.HasValue
            || !HasComp<MechComponent>(component.EquipmentOwner.Value))
        {
            if (!_canUseMechGunOutside)
                args.Cancel();
            return;
        }

        var ev = new HandleMechEquipmentBatteryEvent();
        RaiseLocalEvent(uid, ev);
    }

    private void UpdateAppearance(EntityUid uid, MechComponent? component = null,
        AppearanceComponent? appearance = null)
    {
        if (!Resolve(uid, ref component, ref appearance, false))
            return;

        _appearance.SetData(uid, MechVisuals.Open, IsEmpty(component), appearance);
        _appearance.SetData(uid, MechVisuals.Broken, component.Broken, appearance);
        _appearance.SetData(uid, MechVisuals.EngineOn, component.EngineRunning, appearance); // Forge-Change
    }

    private void OnDragDrop(EntityUid uid, MechComponent component, ref DragDropTargetEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (CanInsert(uid, args.Dragged, component))
        { // Forge-Change
            var doAfterEventArgs = new DoAfterArgs(EntityManager, args.Dragged, component.EntryDelay, new MechEntryEvent(), uid, target: uid)
            {
                BreakOnMove = true,
            };
            _doAfter.TryStartDoAfter(doAfterEventArgs);
        }
        else if (CanInsertPassenger(uid, args.Dragged, component))
        {
            var doAfterEventArgs = new DoAfterArgs(EntityManager, args.Dragged, component.EntryDelay, new MechPassengerEntryEvent(), uid, target: uid)
            {
                BreakOnMove = true,
            };
            _doAfter.TryStartDoAfter(doAfterEventArgs);
        } // Forge-Change
    }

    private void OnCanDragDrop(EntityUid uid, MechComponent component, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= !component.Broken && (CanInsert(uid, args.Dragged, component) || CanInsertPassenger(uid, args.Dragged, component));
    }

    private void OnEmagged(EntityUid uid, MechComponent component, ref GotEmaggedEvent args) // Goobstation
    {
        if (!component.BreakOnEmag)
            return;
        args.Handled = true;
        component.EquipmentWhitelist = null;
        Dirty(uid, component);
    }
}

public sealed partial class MechEjectPassenger1Event : InstantActionEvent;  // Forge-Change
public sealed partial class MechEjectPassenger2Event : InstantActionEvent;  // Forge-Change
public sealed partial class MechEjectPassenger3Event : InstantActionEvent;  // Forge-Change

[Serializable, NetSerializable]
public sealed partial class RemoveBatteryEvent : SimpleDoAfterEvent
{
}
[Serializable, NetSerializable]
public sealed partial class MechExitEvent : SimpleDoAfterEvent
{
}
[Serializable, NetSerializable]
public sealed partial class MechEntryEvent : SimpleDoAfterEvent
{
}
[Serializable, NetSerializable]
public sealed partial class MechPassengerEntryEvent : SimpleDoAfterEvent  // Forge-Change
{
}
[Serializable, NetSerializable]
public sealed partial class RemoveIgnitionKeyEvent : SimpleDoAfterEvent  // Forge-Change
{
}
[Serializable, NetSerializable]
public sealed partial class HandleMechEquipmentBatteryEvent : EntityEventArgs
{
}
