using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Mech.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.EntitySystems;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Verbs;
using Content.Shared.Wires;
using Content.Server.Body.Systems;
using Content.Shared.Tools.Systems;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Content.Shared.Whitelist;
// Forge-Change-start
using Content.Shared.Actions;
using Robust.Shared.Audio.Systems;
using Content.Shared.Implants.Components; // for IgnitionKey? Might need a new component
using Content.Shared.Vehicles;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Atmos;
using Content.Shared.Hands.EntitySystems;
// Forge-Change-end
namespace Content.Server.Mech.Systems;

public sealed partial class MechSystem : SharedMechSystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;
    // Forge-Change-start
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    // Forge-Change-end

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MechComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<MechComponent, EntInsertedIntoContainerMessage>(OnContainerInserted); // Forge-Change
        SubscribeLocalEvent<MechComponent, EntRemovedFromContainerMessage>(OnRemoveIgnition); // Forge-Change
        SubscribeLocalEvent<MechComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MechComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternativeVerb);
        SubscribeLocalEvent<MechComponent, MechOpenUiEvent>(OnOpenUi);
        SubscribeLocalEvent<MechComponent, RemoveBatteryEvent>(OnRemoveBattery);
        SubscribeLocalEvent<MechComponent, RemoveIgnitionKeyEvent>(OnRemoveIgnitionKey); // Forge-Change
        SubscribeLocalEvent<MechComponent, MechEntryEvent>(OnMechEntry);
        SubscribeLocalEvent<MechComponent, MechExitEvent>(OnMechExit);
        SubscribeLocalEvent<MechComponent, MechPassengerEntryEvent>(OnMechPassengerEntry); // Forge-Change
        SubscribeLocalEvent<MechComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MechComponent, MechEquipmentRemoveMessage>(OnRemoveEquipmentMessage);
        SubscribeLocalEvent<MechComponent, UpdateCanMoveEvent>(OnMechCanMoveEvent);
        SubscribeLocalEvent<MechComponent, ActivateInWorldEvent>(OnActivate); // Forge-Change
        SubscribeLocalEvent<MechComponent, MechEjectPassenger1Event>(OnEjectPassenger1); // Forge-Change
        SubscribeLocalEvent<MechComponent, MechEjectPassenger2Event>(OnEjectPassenger2); // Forge-Change
        SubscribeLocalEvent<MechComponent, MechEjectPassenger3Event>(OnEjectPassenger3); // Forge-Change

        SubscribeLocalEvent<MechPilotComponent, ToolUserAttemptUseEvent>(OnToolUseAttempt);
        SubscribeLocalEvent<MechPilotComponent, InhaleLocationEvent>(OnInhale);
        SubscribeLocalEvent<MechPilotComponent, ExhaleLocationEvent>(OnExhale);
        SubscribeLocalEvent<MechPilotComponent, AtmosExposedGetAirEvent>(OnExpose);

        SubscribeLocalEvent<MechAirComponent, GetFilterAirEvent>(OnGetFilterAir);

        #region Equipment UI message relays
        SubscribeLocalEvent<MechComponent, MechGrabberEjectMessage>(ReceiveEquipmentUiMesssages);
        SubscribeLocalEvent<MechComponent, MechSoundboardPlayMessage>(ReceiveEquipmentUiMesssages);
        #endregion
    }
 // Forge-Change-start
    #region Movement & Engine

    private void OnMechCanMoveEvent(EntityUid uid, MechComponent component, UpdateCanMoveEvent args)
    {
        if (component.Broken || component.Integrity <= 0 || component.Energy <= 0 || !component.EngineRunning)
            args.Cancel();
    }
    private void UpdateEngineRunning(EntityUid uid, MechComponent component)
    {
        if (component.PilotSlot.ContainedEntity != null)
        {
            if (component.EngineRunning)
                _mover.SetRelay(component.PilotSlot.ContainedEntity.Value, uid);
            else
                RemComp<RelayInputMoverComponent>(component.PilotSlot.ContainedEntity.Value);
        }
        _actionBlocker.UpdateCanMove(uid);
    }

    #endregion

    #region Interaction (Using items)

    private void OnInteractUsing(EntityUid uid, MechComponent component, InteractUsingEvent args)
    {
        bool isKeyInteraction = HasComp<IgnitionKeyComponent>(args.Used);

        if (!isKeyInteraction && TryComp<WiresPanelComponent>(uid, out var panel) && !panel.Open)
            return;

        if (isKeyInteraction && component.IgnitionSlot.ContainedEntity == null)
        {
            InsertIgnitionKey(uid, args.Used, component);
            _actionBlocker.UpdateCanMove(uid);
            return;
        }
        if (component.BatterySlot.ContainedEntity == null && TryComp<BatteryComponent>(args.Used, out var battery))
        {
            InsertBattery(uid, args.Used, component, battery);
            _actionBlocker.UpdateCanMove(uid);
            return;
        }

        if (_toolSystem.HasQuality(args.Used, "Prying") && component.BatterySlot.ContainedEntity != null)
        {
            if (TryComp<WiresPanelComponent>(uid, out var panelCheck) && !panelCheck.Open)
                return;

            var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.BatteryRemovalDelay,
                new RemoveBatteryEvent(), uid, target: uid, used: args.Target)
            {
                BreakOnMove = true
            };
            _doAfter.TryStartDoAfter(doAfterEventArgs);
            return;
        }
    }
    #endregion

    #region Container handling

    private void OnContainerInserted(EntityUid uid, MechComponent component, EntInsertedIntoContainerMessage args)
    {
        if (args.Container == component.BatterySlot && TryComp<BatteryComponent>(args.Entity, out var battery))
        {
            component.Energy = battery.CurrentCharge;
            component.MaxEnergy = battery.MaxCharge;
            Dirty(uid, component);
            _actionBlocker.UpdateCanMove(uid);
            UpdateUserInterface(uid, component);
            return;
        }
        if (args.Container == component.IgnitionSlot && HasComp<IgnitionKeyComponent>(args.Entity))
        {
            component.EngineRunning = true;
            Dirty(uid, component);
            UpdateEngineRunning(uid, component);
            return;
        }
    }
    private void OnRemoveIgnition(EntityUid uid, MechComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container != component.IgnitionSlot)
            return;

        component.EngineRunning = false;
        Dirty(uid, component);
        UpdateEngineRunning(uid, component);
    }
    #endregion

    #region Ignition key methods
    private void InsertIgnitionKey(EntityUid uid, EntityUid toInsert, MechComponent component)
    {
        if (component.IgnitionSlot.ContainedEntity != null)
            return;
        _container.Insert(toInsert, component.IgnitionSlot);
    }
    private void RemoveIgnitionKey(EntityUid uid, MechComponent component)
    {
        if (component.IgnitionSlot.ContainedEntity == null)
            return;
        _container.EmptyContainer(component.IgnitionSlot);
    }
    private void OnRemoveIgnitionKey(EntityUid uid, MechComponent component, RemoveIgnitionKeyEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;
        RemoveIgnitionKey(uid, component);
        args.Handled = true;
    }
    #endregion

    #region Battery methods
    public void InsertBattery(EntityUid uid, EntityUid toInsert, MechComponent? component = null, BatteryComponent? battery = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (!Resolve(toInsert, ref battery, false))
            return;

        _container.Insert(toInsert, component.BatterySlot);
        component.Energy = battery.CurrentCharge;
        component.MaxEnergy = battery.MaxCharge;
        _actionBlocker.UpdateCanMove(uid);

        Dirty(uid, component);
        UpdateUserInterface(uid, component);
    }
    public void RemoveBattery(EntityUid uid, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        _container.EmptyContainer(component.BatterySlot);
        component.Energy = 0;
        component.MaxEnergy = 0;

        _actionBlocker.UpdateCanMove(uid);

        Dirty(uid, component);
        UpdateUserInterface(uid, component);
    }

    private void OnRemoveBattery(EntityUid uid, MechComponent component, RemoveBatteryEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        RemoveBattery(uid, component);
        _actionBlocker.UpdateCanMove(uid);

        args.Handled = true;
    }
    #endregion

    #region MapInit, Equipment, Verbs, UI

    private void OnMapInit(EntityUid uid, MechComponent component, MapInitEvent args)
    {
        var xform = Transform(uid);
        foreach (var equipment in component.StartingEquipment)
        {
            var ent = Spawn(equipment, xform.Coordinates);
            InsertEquipment(uid, ent, component);
        }

        component.Integrity = component.MaxIntegrity;
        component.Energy = component.MaxEnergy;

        if (component.Airtight && !HasComp<MechAirComponent>(uid))
        {
            var airComp = AddComp<MechAirComponent>(uid);
            // Создаём стандартную атмосферу Земли (21% O2, 79% N2, 101.325 кПа, 293.15 K)
            var volume = airComp.Air.Volume; // = 70 литров (из компонента)
            var temp = Atmospherics.T20C; // 293.15 K
            var pressure = Atmospherics.OneAtmosphere; // 101.325 кПа
            var totalMoles = (pressure * volume) / (Atmospherics.R * temp); // уравнение Менделеева-Клапейрона

            airComp.Air.AdjustMoles(Gas.Oxygen, totalMoles * 0.21f);
            airComp.Air.AdjustMoles(Gas.Nitrogen, totalMoles * 0.79f);
            airComp.Air.Temperature = temp;
        }

        _actionBlocker.UpdateCanMove(uid);
        Dirty(uid, component);
    }
 // Forge-Change-end
    private void OnRemoveEquipmentMessage(EntityUid uid, MechComponent component, MechEquipmentRemoveMessage args)
    {
        var equip = GetEntity(args.Equipment);

        if (!Exists(equip) || Deleted(equip))
            return;

        if (!component.EquipmentContainer.ContainedEntities.Contains(equip))
            return;

        RemoveEquipment(uid, equip, component);
    }

    private void OnOpenUi(EntityUid uid, MechComponent component, MechOpenUiEvent args)
    {
        args.Handled = true;
        ToggleMechUi(uid, component);
    }

    private void OnToolUseAttempt(EntityUid uid, MechPilotComponent component, ref ToolUserAttemptUseEvent args)
    {
        if (args.Target == component.Mech)
            args.Cancelled = true;
    }

    private void OnAlternativeVerb(EntityUid uid, MechComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || component.Broken)
            return;

        if (CanInsert(uid, args.User, component))
        {
            var enterVerb = new AlternativeVerb
            {
                Text = Loc.GetString("mech-verb-enter"),
                Act = () =>
                {
                    var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.EntryDelay, new MechEntryEvent(), uid, target: uid)
                    {
                        BreakOnMove = true,
                    };
                    _doAfter.TryStartDoAfter(doAfterEventArgs);
                }
            };
 // Forge-Change-start
            args.Verbs.Add(enterVerb);
        }
        else if (!IsEmpty(component) && CanInsertPassenger(uid, args.User, component))
        {
            var passengerVerb = new AlternativeVerb
            {
                Text = Loc.GetString("mech-verb-enter-passenger"),
                Act = () =>
                {
                    var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.EntryDelay, new MechPassengerEntryEvent(), uid, target: uid)
                    {
                        BreakOnMove = true,
                    };
                    _doAfter.TryStartDoAfter(doAfterEventArgs);
                }
            };
            args.Verbs.Add(passengerVerb);
        }

        var openUiVerb = new AlternativeVerb
        {
            Act = () => ToggleMechUi(uid, component, args.User),
            Text = Loc.GetString("mech-ui-open-verb")
        };
        args.Verbs.Add(openUiVerb);

        if (!IsEmpty(component) || !IsPassengerEmpty(component))
        {
            var ejectVerb = new AlternativeVerb
            {
                Text = Loc.GetString("mech-verb-exit"),
                Priority = 1,
                Act = () =>
                {
                    if (component.PilotSlot.ContainedEntity == args.User)
                    {
                        TryEject(uid, component);
                        return;
                    }
                    if (IsPassenger(args.User, component))
                    {
                        TryEjectPassenger(uid, args.User, component);
                        return;
                    }
                    if (args.User == uid)
                        return;

                    var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.ExitDelay,
                        new MechExitEvent(), uid, target: uid);
                    _doAfter.TryStartDoAfter(doAfterEventArgs);
                }
            };
            args.Verbs.Add(ejectVerb);
        }
    }
    private bool IsPassengerEmpty(MechComponent component)
    {
        return component.PassengerSlot1.ContainedEntity == null &&
               component.PassengerSlot2.ContainedEntity == null &&
               component.PassengerSlot3.ContainedEntity == null;
    }
    private new bool IsPassenger(EntityUid uid, MechComponent component)
    {
        return component.PassengerSlot1.ContainedEntity == uid ||
               component.PassengerSlot2.ContainedEntity == uid ||
               component.PassengerSlot3.ContainedEntity == uid;
    }

    private void OnMechEntry(EntityUid uid, MechComponent component, MechEntryEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (_whitelistSystem.IsWhitelistFail(component.PilotWhitelist, args.User))
        {
            _popup.PopupEntity(Loc.GetString("mech-no-enter", ("item", uid)), args.User);
            return;
        }

        if (!TryInsert(uid, args.User, component))
        {
            _popup.PopupEntity("Cannot enter mech!", args.User);
            return;
        }

        _actionBlocker.UpdateCanMove(uid);
        args.Handled = true;
    }
    private void OnMechPassengerEntry(EntityUid uid, MechComponent component, MechPassengerEntryEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!TryInsertPassenger(uid, args.User, component))
        {
            _popup.PopupEntity("Cannot enter as passenger!", args.User);
            return;
        }

        _popup.PopupEntity("You enter as passenger.", args.User);
        args.Handled = true;
    }

    private void OnMechExit(EntityUid uid, MechComponent component, MechExitEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (component.PilotSlot.ContainedEntity == args.User)
        {
            TryEject(uid, component);
        }
        else if (IsPassenger(args.User, component))
        {
            TryEjectPassenger(uid, args.User, component);
        }
        else
        {
            foreach (var slot in GetPassengerSlots(component))
            {
                if (slot.ContainedEntity != null)
                {
                    TryEjectPassenger(uid, slot.ContainedEntity.Value, component);
                    args.Handled = true;
                    return;
                }
            }
            if (component.PilotSlot.ContainedEntity != null)
            {
                TryEject(uid, component);
            }
        }
        args.Handled = true;
    }
 // Forge-Change-end
    private void OnDamageChanged(EntityUid uid, MechComponent component, DamageChangedEvent args)
    {
        var integrity = component.MaxIntegrity - args.Damageable.TotalDamage;
        SetIntegrity(uid, integrity, component);

        if (args.DamageIncreased &&
            args.DamageDelta != null &&
            component.PilotSlot.ContainedEntity != null)
        {
            var damage = args.DamageDelta * component.MechToPilotDamageMultiplier;
            _damageable.TryChangeDamage(component.PilotSlot.ContainedEntity, damage);
        }
    }

    private void ToggleMechUi(EntityUid uid, MechComponent? component = null, EntityUid? user = null)
    {
        if (!Resolve(uid, ref component))
            return;
        user ??= component.PilotSlot.ContainedEntity;
        if (user == null)
            return;

        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        _ui.TryToggleUi(uid, MechUiKey.Key, actor.PlayerSession);
        UpdateUserInterface(uid, component);
    }

    private void ReceiveEquipmentUiMesssages<T>(EntityUid uid, MechComponent component, T args) where T : MechEquipmentUiMessage
    {
        var ev = new MechEquipmentUiMessageRelayEvent(args);
        var allEquipment = new List<EntityUid>(component.EquipmentContainer.ContainedEntities);
        var argEquip = GetEntity(args.Equipment);

        foreach (var equipment in allEquipment)
        {
            if (argEquip == equipment)
                RaiseLocalEvent(equipment, ev);
        }
    }

    public override void UpdateUserInterface(EntityUid uid, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        base.UpdateUserInterface(uid, component);

        var ev = new MechEquipmentUiStateReadyEvent();
        foreach (var ent in component.EquipmentContainer.ContainedEntities)
        {
            RaiseLocalEvent(ent, ev);
        }

        var state = new MechBoundUiState
        {
            EquipmentStates = ev.States
        };
        _ui.SetUiState(uid, MechUiKey.Key, state);
    }

    public override void BreakMech(EntityUid uid, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;
 // Forge-Change
        base.BreakMech(uid, component);

        if (component.IgnitionSlot.ContainedEntity != null)
            RemoveIgnitionKey(uid, component);
// Forge-Change
        _ui.CloseUi(uid, MechUiKey.Key);
        _actionBlocker.UpdateCanMove(uid);
    }

    public override bool TryChangeEnergy(EntityUid uid, FixedPoint2 delta, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (!base.TryChangeEnergy(uid, delta, component))
            return false;

        var battery = component.BatterySlot.ContainedEntity;
        if (battery == null)
            return false;

        if (!TryComp<BatteryComponent>(battery, out var batteryComp))
            return false;

        _battery.SetCharge(battery!.Value, batteryComp.CurrentCharge + delta.Float(), batteryComp);
        if (batteryComp.CurrentCharge != component.Energy) // Forge-Change
        {
            Log.Debug($"Battery charge was not equal to mech charge. Battery {batteryComp.CurrentCharge}. Mech {component.Energy}");
            component.Energy = batteryComp.CurrentCharge;
            Dirty(uid, component);
        }
        _actionBlocker.UpdateCanMove(uid);
        return true;
    }

    #endregion

    #region Atmos Handling
    private void OnInhale(EntityUid uid, MechPilotComponent component, InhaleLocationEvent args)
    {
        if (!TryComp<MechComponent>(component.Mech, out var mech) ||
            !TryComp<MechAirComponent>(component.Mech, out var mechAir))
        {
            return;
        }

        if (mech.Airtight)
            args.Gas = mechAir.Air;
    }

    private void OnExhale(EntityUid uid, MechPilotComponent component, ExhaleLocationEvent args)
    {
        if (!TryComp<MechComponent>(component.Mech, out var mech) ||
            !TryComp<MechAirComponent>(component.Mech, out var mechAir))
        {
            return;
        }

        if (mech.Airtight)
            args.Gas = mechAir.Air;
    }

    private void OnExpose(EntityUid uid, MechPilotComponent component, ref AtmosExposedGetAirEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(component.Mech, out MechComponent? mech))
            return;

        if (mech.Airtight && TryComp(component.Mech, out MechAirComponent? air))
        {
            args.Handled = true;
            args.Gas = air.Air;
            return;
        }

        args.Gas = _atmosphere.GetContainingMixture(component.Mech, excite: args.Excite); // Forge-Change
        args.Handled = true;
    }

    private void OnGetFilterAir(EntityUid uid, MechAirComponent comp, ref GetFilterAirEvent args)
    {
        if (args.Air != null)
            return;

        if (!TryComp<MechComponent>(uid, out var mech) || !mech.Airtight)
            return;

        args.Air = comp.Air;
    }
    #endregion
 // Forge-Change-start
    #region Horn & Siren (actions from Vehicle)
    protected override void OnHornAction(EntityUid uid, MechComponent component, HornActionEvent args)
    {
        base.OnHornAction(uid, component, args);
        if (args.Handled)
            return;
        if (component.PilotSlot.ContainedEntity != args.Performer)
            return;
        if (component.HornSound == null)
            return;
        _audio.PlayPvs(component.HornSound, uid);
        args.Handled = true;
    }
    protected override void OnSirenAction(EntityUid uid, MechComponent component, SirenActionEvent args)
    {
        base.OnSirenAction(uid, component, args);
        if (args.Handled)
            return;
        if (component.PilotSlot.ContainedEntity != args.Performer)
            return;
        if (component.SirenSound == null)
            return;
        if (component.SirenEnabled)
        {
            component.SirenStream = _audio.Stop(component.SirenStream);
        }
        else
        {
            component.SirenStream = _audio.PlayPvs(component.SirenSound, uid)?.Entity;
        }
        component.SirenEnabled = !component.SirenEnabled;
        args.Handled = true;
    }
    #endregion
    private void OnActivate(EntityUid uid, MechComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<MechPilotComponent>(args.User) || HasComp<MechPassengerComponent>(args.User))
            return;

        if (component.IgnitionSlot.ContainedEntity is not { } key)
            return;

        if (_hands.TryPickupAnyHand(args.User, key))
        {
            _popup.PopupEntity(Loc.GetString("mech-key-removed"), uid, args.User);
        }
        else
        {
            Transform(key).Coordinates = Transform(uid).Coordinates;
            _popup.PopupEntity(Loc.GetString("mech-key-dropped"), uid, args.User);
        }

        _container.Remove(key, component.IgnitionSlot);
        args.Handled = true;
 }
    private void OnEjectPassenger1(EntityUid uid, MechComponent component, MechEjectPassenger1Event args)
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
    private void OnEjectPassenger2(EntityUid uid, MechComponent component, MechEjectPassenger2Event args)
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
    private void OnEjectPassenger3(EntityUid uid, MechComponent component, MechEjectPassenger3Event args)
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
}
