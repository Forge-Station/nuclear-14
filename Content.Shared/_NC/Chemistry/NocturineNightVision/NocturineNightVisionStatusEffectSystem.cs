using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Overlays.Switchable;


namespace Content.Shared.Chemistry;


public sealed class NocturineNightVisionStatusEffectSystem : EntitySystem
{
    public const string StatusKey = "NocturineNightVision";

    private static readonly string[] VisionSlots =
    {
        "eyes",
        "mask"
    };

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, DidEquipEvent>(OnDidEquip);
        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, DidUnequipEvent>(OnDidUnequip);
    }

    private void OnStartup(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ComponentStartup args) =>
        Reconcile(uid, comp);

    private void OnShutdown(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ComponentShutdown args) =>
        Restore(uid, comp);

    private void OnDidEquip(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ref DidEquipEvent args)
    {
        if (!IsVisionSlot(args.Slot))
            return;

        Reconcile(uid, comp);
    }

    public void ForceReconcile(EntityUid wearer)
    {
        if (!TryComp(wearer, out NocturineNightVisionStatusEffectComponent? meta))
            return;

        Reconcile(wearer, meta);
    }

    private void OnDidUnequip(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ref DidUnequipEvent args)
    {
        if (!IsVisionSlot(args.Slot))
            return;

        Reconcile(uid, comp);
    }

    private static bool IsVisionSlot(string slot)
    {
        for (var i = 0; i < VisionSlots.Length; i++)
            if (VisionSlots[i] == slot)
                return true;

        return false;
    }

    private void Reconcile(EntityUid wearer, NocturineNightVisionStatusEffectComponent meta)
    {
        if (HasActiveEquippedNightVision(wearer))
            SuppressChemical(wearer, meta);
        else
            ApplyChemical(wearer, meta);
    }

    private bool HasActiveEquippedNightVision(EntityUid wearer)
    {
        for (var i = 0; i < VisionSlots.Length; i++)
        {
            var slot = VisionSlots[i];

            if (!_inventory.TryGetSlotEntity(wearer, slot, out var slotEnt))
                continue;

            if (!TryComp(slotEnt, out NightVisionComponent? nv))
                continue;

            if (nv.IsEquipment && nv.IsActive)
                return true;
        }

        return false;
    }

    private void ApplyChemical(EntityUid wearer, NocturineNightVisionStatusEffectComponent meta)
    {
        if (!TryComp(wearer, out NightVisionComponent? nv))
        {
            nv = EnsureComp<NightVisionComponent>(wearer);
            meta.AddedNightVision = true;
        }

        if (!meta.AddedNightVision && !meta.SavedOriginal)
        {
            meta.SavedOriginal = true;
            meta.OriginalIsActive = nv.IsActive;
            meta.OriginalColor = nv.Color;
        }

        nv.IsEquipment = false;
        nv.IsActive = true;
        nv.Color = meta.NightVisionColor;

        RemoveToggleActionIfAny(wearer, nv);
    }

    private void SuppressChemical(EntityUid wearer, NocturineNightVisionStatusEffectComponent meta)
    {
        if (!TryComp(wearer, out NightVisionComponent? nv))
            return;

        if (meta.AddedNightVision)
        {
            nv.IsActive = false;
            RemoveToggleActionIfAny(wearer, nv);
            return;
        }

        if (meta.SavedOriginal)
        {
            nv.IsActive = meta.OriginalIsActive;
            nv.Color = meta.OriginalColor;
        }
        else
            nv.IsActive = false;

        RemoveToggleActionIfAny(wearer, nv);
    }

    private void Restore(EntityUid wearer, NocturineNightVisionStatusEffectComponent meta)
    {
        if (!TryComp(wearer, out NightVisionComponent? nv))
            return;

        if (meta.AddedNightVision)
        {
            RemoveToggleActionIfAny(wearer, nv);
            RemComp<NightVisionComponent>(wearer);
            return;
        }

        if (meta.SavedOriginal)
        {
            nv.IsActive = meta.OriginalIsActive;
            nv.Color = meta.OriginalColor;
        }

        RemoveToggleActionIfAny(wearer, nv);
    }

    private void RemoveToggleActionIfAny(EntityUid wearer, NightVisionComponent nv)
    {
        nv.ToggleAction = null;

        if (nv.ToggleActionEntity == null)
            return;

        _actions.RemoveAction(wearer, nv.ToggleActionEntity);
        nv.ToggleActionEntity = null;
    }
}
