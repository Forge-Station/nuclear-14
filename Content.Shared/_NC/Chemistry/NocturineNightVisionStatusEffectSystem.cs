using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Overlays.Switchable;
using Robust.Shared.Network;

namespace Content.Shared.Chemistry;

public sealed class NocturineNightVisionStatusEffectSystem : EntitySystem
{
    public const string StatusKey = "NocturineNightVision";

    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    private static readonly string[] VisionSlots =
    {
        "eyes",
        "head",
        "mask",
    };

    public override void Initialize()
    {
        base.Initialize();
        if (!_net.IsServer)
            return;
        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, DidEquipEvent>(OnDidEquip);
        SubscribeLocalEvent<NocturineNightVisionStatusEffectComponent, DidUnequipEvent>(OnDidUnequip);
        SubscribeLocalEvent<NightVisionComponent, SwitchableOverlayToggledEvent>(OnNightVisionToggled);
    }

    public void Refresh(EntityUid uid, Color color)
    {
        if (!_net.IsServer)
            return;

        if (!TryComp(uid, out NocturineNightVisionStatusEffectComponent? comp))
            return;

        comp.NightVisionColor = color;
        Reconcile(uid, comp);
    }

    private void OnStartup(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ComponentStartup args)
    {
        Reconcile(uid, comp);
    }

    private void OnShutdown(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ComponentShutdown args)
    {
        Restore(uid, comp);
    }

    private void OnDidEquip(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ref DidEquipEvent args)
    {
        if (!IsVisionSlot(args.Slot))
            return;

        Reconcile(uid, comp);
    }

    private void OnDidUnequip(EntityUid uid, NocturineNightVisionStatusEffectComponent comp, ref DidUnequipEvent args)
    {
        if (!IsVisionSlot(args.Slot))
            return;

        Reconcile(uid, comp);
    }

    private bool IsVisionSlot(string slot)
    {
        return slot is "eyes" or "head" or "mask";
    }

    private void OnNightVisionToggled(EntityUid item, NightVisionComponent comp, ref SwitchableOverlayToggledEvent args)
    {
        if (!comp.IsEquipment)
            return;

        var wearer = args.User;

        if (!TryComp(wearer, out NocturineNightVisionStatusEffectComponent? meta))
            return;

        Reconcile(wearer, meta);
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
        foreach (var slot in VisionSlots)
        {
            if (!_inventory.TryGetSlotEntity(wearer, slot, out EntityUid? item))
                continue;

            if (!TryComp(item.Value, out NightVisionComponent? nv))
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

            nv.ToggleAction = null;
            if (nv.ToggleActionEntity != null)
            {
                _actions.RemoveAction(wearer, nv.ToggleActionEntity);
                nv.ToggleActionEntity = null;
            }
        }
        else
        {
            if (nv.IsEquipment)
                return;

            if (!meta.AddedNightVision && !meta.SavedOriginal)
            {
                meta.OriginalIsActive = nv.IsActive;
                meta.OriginalColor = nv.Color;
                meta.SavedOriginal = true;
            }
        }

        var dirty = false;

        if (!nv.IsActive)
        {
            nv.IsActive = true;
            dirty = true;
        }

        if (nv.Color != meta.NightVisionColor)
        {
            nv.Color = meta.NightVisionColor;
            dirty = true;
        }

        if (dirty)
            Dirty(wearer, nv);
    }

    private void SuppressChemical(EntityUid wearer, NocturineNightVisionStatusEffectComponent meta)
    {
        if (!TryComp(wearer, out NightVisionComponent? nv))
            return;

        if (nv.IsEquipment)
            return;

        var dirty = false;

        if (meta.AddedNightVision)
        {
            if (nv.IsActive)
            {
                nv.IsActive = false;
                dirty = true;
            }
        }
        else if (meta.SavedOriginal)
        {
            if (nv.IsActive != meta.OriginalIsActive)
            {
                nv.IsActive = meta.OriginalIsActive;
                dirty = true;
            }

            if (nv.Color != meta.OriginalColor)
            {
                nv.Color = meta.OriginalColor;
                dirty = true;
            }
        }

        if (dirty)
            Dirty(wearer, nv);
    }

    private void Restore(EntityUid wearer, NocturineNightVisionStatusEffectComponent meta)
    {
        if (!TryComp(wearer, out NightVisionComponent? nv))
            return;

        if (nv.IsEquipment)
            return;

        if (meta.AddedNightVision)
        {
            if (nv.IsActive)
            {
                nv.IsActive = false;
                Dirty(wearer, nv);
            }

            if (nv.ToggleActionEntity != null)
            {
                _actions.RemoveAction(wearer, nv.ToggleActionEntity);
                nv.ToggleActionEntity = null;
            }

            RemComp<NightVisionComponent>(wearer);
            return;
        }

        if (!meta.SavedOriginal)
            return;

        var dirty = false;

        if (nv.IsActive != meta.OriginalIsActive)
        {
            nv.IsActive = meta.OriginalIsActive;
            dirty = true;
        }

        if (nv.Color != meta.OriginalColor)
        {
            nv.Color = meta.OriginalColor;
            dirty = true;
        }

        if (dirty)
            Dirty(wearer, nv);
    }
}
