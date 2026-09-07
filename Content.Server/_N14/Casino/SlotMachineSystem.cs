using Content.Server.Power.EntitySystems;
using Content.Shared._N14.Casino;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._N14.Casino;

public sealed class SlotMachineSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedStackSystem _stackSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public const string BottleCapStackType = "Caps";

    private static readonly AudioParams QuietSoundRange = new() { MaxDistance = 3.5f, Volume = -6f };

    private static readonly AudioParams MidQuietSoundRange = new() { MaxDistance = 3.5f, Volume = -3f };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SlotMachineComponent, ActivateInWorldEvent>(OnInteractHandEvent);
        SubscribeLocalEvent<SlotMachineComponent, SlotMachineDoAfterEvent>(OnSlotMachineDoAfter);
    }

    private void OnInteractHandEvent(EntityUid uid, SlotMachineComponent comp, ActivateInWorldEvent args)
    {
        if (comp.IsSpinning || !_power.IsPowered(uid))
            return;

        if (!_itemSlots.TryGetSlot(uid, "money", out var slot)
            || slot.Item == null
            || !TryComp<StackComponent>(slot.Item.Value, out var stack)
            || stack.StackTypeId != BottleCapStackType
            || stack.Count < comp.SpinCost)
        {
            _popupSystem.PopupEntity(Loc.GetString("slotmachine-no-money"), uid, args.User, PopupType.Small);
            return;
        }

        _stackSystem.SetCount(stack.Owner, stack.Count - comp.SpinCost, stack);
        Dirty(stack.Owner, stack);
        comp.IsSpinning = true;
        Dirty(uid, comp);

        _audio.PlayPvs(comp.SpinSound, uid, QuietSoundRange);

        if (TryComp<AppearanceComponent>(uid, out var appearance))
        {
            _appearance.SetData(uid, SlotMachineVisuals.Spinning, true, appearance);
        }

        var doAfter =
            new DoAfterArgs(EntityManager, uid, comp.DoAfterTime, new SlotMachineDoAfterEvent(), uid)
            {
                BreakOnMove = false,
                BreakOnDamage = false,
                MultiplyDelay = false,
            };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnSlotMachineDoAfter(EntityUid uid, SlotMachineComponent comp, SlotMachineDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            comp.IsSpinning = false;
            Dirty(uid, comp);
            return;
        }

        if (args.Handled || !_itemSlots.TryGetSlot(uid, "money", out var slot))
            return;

        comp.IsSpinning = false;
        Dirty(uid, comp);

        if (TryComp<AppearanceComponent>(uid, out var appearance))
        {
            _appearance.SetData(uid, SlotMachineVisuals.Spinning, false, appearance);
        }

        if (slot.Item != null && TryComp<StackComponent>(slot.Item.Value, out var stack) && stack.StackTypeId == BottleCapStackType)
        {
            if (_random.Prob(comp.SmallWinChance))
            {
                _audio.PlayPvs(comp.SmallWinSound, uid, QuietSoundRange);
                HandlePrize(uid, comp.SmallPrizeAmount);
                return;
            }
            if (_random.Prob(comp.MediumWinChance))
            {
                _audio.PlayPvs(comp.MediumWinSound, uid, QuietSoundRange);
                HandlePrize(uid, comp.MediumPrizeAmount);
                return;
            }
            if (_random.Prob(comp.BigWinChance))
            {
                _audio.PlayPvs(comp.BigWinSound, uid, QuietSoundRange);
                HandlePrize(uid, comp.BigPrizeAmount);
                return;
            }
            if (_random.Prob(comp.JackPotWinChance))
            {
                _audio.PlayPvs(comp.JackPotWinSound, uid, QuietSoundRange);
                HandlePrize(uid, comp.JackPotPrizeAmount);
                return;
            }
        }

        _audio.PlayPvs(comp.LoseSound, uid, MidQuietSoundRange);
    }

    private void HandlePrize(EntityUid uid, int prize)
    {
        var coordinates = Transform(uid).Coordinates;
        var remaining = prize;

        while (remaining > 0)
        {
            var caps = EntityManager.SpawnEntity("N14CurrencyCap", coordinates);
            var amount = Math.Min(remaining, _stackSystem.GetMaxCount("N14CurrencyCap"));
            _stackSystem.SetCount(caps, amount);
            remaining -= amount;
        }
    }
}
