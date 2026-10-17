using Content.Shared.Actions;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.SelfExtinguisher;

public abstract partial class SharedSelfExtinguisherSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedChargesSystem _charges = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SelfExtinguisherComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<SelfExtinguisherComponent, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>>>(GetRelayedVerbs);
        SubscribeLocalEvent<SelfExtinguisherComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<SelfExtinguisherComponent, GetVerbsEvent<EquipmentVerb>>(OnGetVerbs);

        SubscribeLocalEvent<SelfExtinguisherComponent, InteractUsingEvent>(OnInteractUsing);

        SubscribeLocalEvent<SelfExtinguisherComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, SelfExtinguisherComponent component, MapInitEvent args)
    {
        if (!_actionContainer.EnsureAction(uid, ref component.ActionEntity, out _, component.Action))
            return;

        // The components SelfExtinguisherComponent and LimitedChargesComponent will be the source of truth
        // regarding the cooldowns and charges, and the action component will just mirror any changes.
        _actions.SetUseDelay(component.ActionEntity, component.Cooldown);
        if (TryComp<LimitedChargesComponent>(uid, out var charges))
            MirrorCharges(component.ActionEntity, (uid, charges));
    }

    public void SetCharges(EntityUid uid, int? charges, int? maxCharges, SelfExtinguisherComponent? component = null)
    {
        if (!Resolve(uid, ref component) ||
            !TryComp<LimitedChargesComponent>(uid, out var chargeComp))
            return;

        if (maxCharges != null)
            _charges.SetMaxCharges((uid, chargeComp), maxCharges.Value);

        if (charges != null)
            _charges.SetCharges((uid, chargeComp), charges.Value);

        MirrorCharges(component.ActionEntity, (uid, chargeComp));
    }

    /// <summary>
    /// Действие только отражает заряды самого предмета: источник истины -
    /// его LimitedCharges, как и было до переезда на апстримную систему зарядов.
    /// </summary>
    private void MirrorCharges(EntityUid? action, Entity<LimitedChargesComponent> source)
    {
        if (action == null)
            return;

        var current = _charges.GetCurrentCharges((source.Owner, source.Comp));

        if (TryComp<LimitedChargesComponent>(action, out var actionCharges))
        {
            _charges.SetMaxCharges((action.Value, actionCharges), source.Comp.MaxCharges);
            _charges.SetCharges((action.Value, actionCharges), current);
        }

        _actions.SetEnabled(action, current != 0);
    }

    private void GetRelayedVerbs(EntityUid uid, SelfExtinguisherComponent component, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>> args)
    {
        OnGetVerbs(uid, component, args.Args);
    }

    private void OnGetVerbs(EntityUid uid, SelfExtinguisherComponent component, GetVerbsEvent<EquipmentVerb> args)
    {
        if (!_inventory.TryGetContainingSlot(uid, out var _))
            return;

        var verb = new EquipmentVerb()
        {
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/extinguisher.svg.192dpi.png")),
            Text = Loc.GetString("self-extinguisher-verb"),
            EventTarget = uid,
            ExecutionEventArgs = new SelfExtinguishEvent() { Performer = args.User }
        };

        args.Verbs.Add(verb);
    }

    private void OnGetActions(EntityUid uid, SelfExtinguisherComponent component, GetItemActionsEvent args)
    {
        if (component.ActionEntity == null || args.InHands)
            return;

        args.AddAction(component.ActionEntity.Value);
    }

    private void OnInteractUsing(EntityUid uid, SelfExtinguisherComponent component, InteractUsingEvent args)
    {
        if (!TryComp<SelfExtinguisherRefillComponent>(args.Used, out var refill) ||
            !TryComp<LimitedChargesComponent>(uid, out var charges))
            return;

        if (_charges.GetCurrentCharges((uid, charges)) >= charges.MaxCharges)
        {
            if (!SetPopupCooldown((uid, component)))
                return;

            _popup.PopupClient(Loc.GetString("self-extinguisher-refill-full"), args.User, args.User);
            return;
        }

        // Add charges
        _charges.AddCharges(uid, refill.RefillAmount);

        // Mirror the new charges onto the action and reenable it
        MirrorCharges(component.ActionEntity, (uid, charges));

        // Reset cooldown
        _actions.ClearCooldown(component.ActionEntity);
        component.NextExtinguish = TimeSpan.Zero;

        Dirty(uid, component);

        _popup.PopupClient(Loc.GetString("self-extinguisher-refill"), args.User, args.User);
        _audio.PlayPredicted(component.RefillSound, uid, args.User);

        QueueDel(args.Used);
    }

    private void OnExamined(EntityUid uid, SelfExtinguisherComponent component, ExaminedEvent args)
    {
        if (TryComp<LimitedChargesComponent>(uid, out var charges) && _charges.IsEmpty((uid, charges)))
            return;

        var curTime = _timing.CurTime;
        if (component.NextExtinguish > curTime)
        {
            args.PushMarkup(Loc.GetString("self-extinguisher-examine-cooldown-recharging",
                ("cooldown", Math.Ceiling((component.NextExtinguish - curTime).TotalSeconds))));
            return;
        }

        args.PushMarkup(Loc.GetString("self-extinguisher-examine-cooldown-ready"));
    }

    // <summary>
    //   Returns:
    //   - true if a popup is ready to be shown. The popup cooldown is also set.
    //   - false if popups are still on cooldown
    // </summary>
    protected bool SetPopupCooldown(Entity<SelfExtinguisherComponent> ent, TimeSpan? curTime = null)
    {
        curTime ??= _timing.CurTime;

        if (curTime < ent.Comp.NextPopup)
            return false;

        ent.Comp.NextPopup = curTime + ent.Comp.PopupCooldown;
        return true;
    }
}

// <summary>
//   Raised on an attempt to self-extinguish.
// </summary>
public sealed partial class SelfExtinguishEvent : InstantActionEvent { }
