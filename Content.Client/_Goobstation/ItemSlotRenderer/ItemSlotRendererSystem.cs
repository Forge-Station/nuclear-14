// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Roudenn <romabond091@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;

namespace Content.Goobstation.Client.ItemSlotRenderer;

/// <summary>
/// Renders whatever item is inside the mapped item slot onto sprite layers of the entity.
/// Multi-layer items (e.g. FoodSequence dishes like soups and pies) are fully copied,
/// with extra layers added on top of the mapped layer.
/// Updates are event-driven off item-slot changes rather than polled every frame.
/// </summary>
public sealed class ItemSlotRendererSystem : EntitySystem
{
    [Dependency] private readonly IReflectionManager _reflection = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private readonly ISawmill _log = Logger.GetSawmill("item_slot_renderer");
    private readonly Dictionary<string, (Texture Texture, Vector2 Scale)> _protoTextureCache = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ItemSlotRendererComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ItemSlotRendererComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<ItemSlotRendererComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<ItemSlotRendererComponent, EntRemovedFromContainerMessage>(OnContainerChanged);

        // Cached prototype textures/scales become stale after a prototype hot-reload.
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _protoTextureCache.Clear();
    }

    private void OnRemove(EntityUid uid, ItemSlotRendererComponent comp, ComponentRemove args)
    {
        comp.LayerMappings.Clear();
    }

    private void OnStartup(EntityUid uid, ItemSlotRendererComponent comp, ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
        {
            _log.Error($"ItemSlotRendererComponent requires SpriteComponent to work, but {ToPrettyString(uid)} did not have one. Removing ItemSlotRenderer.");
            RemComp<ItemSlotRendererComponent>(uid);
            return;
        }

        comp.LayerMappings.Clear();

        foreach (var (slotId, layerKeyString) in comp.PrototypeLayerMappings)
        {
            object mapKey = layerKeyString;
            if (_reflection.TryParseEnumReference(layerKeyString, out var e, shouldThrow: false))
                mapKey = e;

            if (!sprite.LayerMapTryGet(mapKey, out _) && comp.ErrorOnMissing)
            {
                _log.Warning($"ItemSlotRenderer: Tried to add a missing layer under the key {mapKey}. Skipping missing layer. If this is unwanted, set component's ErrorOnMissing to false.");
                continue;
            }

            comp.LayerMappings.Add((mapKey, slotId));
        }

        UpdateVisuals(uid, sprite, comp);
    }

    private void OnContainerChanged<T>(EntityUid uid, ItemSlotRendererComponent comp, T args) where T : ContainerModifiedMessage
    {
        // Only react to the slots this renderer actually maps.
        if (!comp.LayerMappings.Any(mapping => mapping.Item2 == args.Container.ID))
            return;

        if (TryComp<SpriteComponent>(uid, out var sprite))
            UpdateVisuals(uid, sprite, comp);
    }

    private void UpdateVisuals(EntityUid uid, SpriteComponent sprite, ItemSlotRendererComponent comp)
    {
        foreach (var (layerKey, slotId) in comp.LayerMappings)
        {
            if (!sprite.LayerMapTryGet(layerKey, out var layerIndex))
                continue;

            var item = _itemSlots.GetItemOrNull(uid, slotId);
            if (item == null)
            {
                ClearVisuals(uid, sprite, comp, layerIndex);
                continue;
            }

            // Collect all usable layers of the item (soups/pies have multiple: bowl + contents).
            // Base prototype layers have no per-layer RSI/Texture - they resolve through the
            // component's base RSI into ActualState, so prefer that when reading.
            List<(SpriteComponent.Layer Src, Texture Tex)>? usable = null;
            SpriteComponent? itemSprite = null;
            if (TryComp<SpriteComponent>(item, out var itemSpriteComp))
            {
                itemSprite = itemSpriteComp;
                foreach (var layer in itemSpriteComp.AllLayers)
                {
                    var src = (SpriteComponent.Layer) layer;
                    Texture? tex = null;

                    if (src.RSI != null && !string.IsNullOrEmpty(src.State.Name)
                        && src.RSI.TryGetState(src.State.Name, out var rsiState))
                        tex = rsiState.Frame0;
                    else if (src.ActualState != null)
                        tex = src.ActualState.Frame0;
                    else if (src.Texture != null && src.Texture != Texture.Transparent)
                        tex = src.Texture;

                    if (tex == null || tex == Texture.Transparent)
                        continue;

                    usable ??= new List<(SpriteComponent.Layer, Texture)>();
                    usable.Add((src, tex));
                }
            }

            if (usable == null)
            {
                // The item's own sprite is empty (items inside containers can have their
                // layers cleared client-side), fall back to the prototype's icon.
                var meta = EntityManager.GetComponent<MetaDataComponent>(item.Value);
                if (meta.EntityPrototype == null)
                {
                    ClearVisuals(uid, sprite, comp, layerIndex);
                    continue;
                }

                if (!_protoTextureCache.TryGetValue(meta.EntityPrototype.ID, out var protoData))
                {
                    var protoScale = Vector2.One;
                    if (meta.EntityPrototype.TryGetComponent("Sprite", out SpriteComponent? protoSpriteComp)
                        && protoSpriteComp != null)
                    {
                        protoScale = protoSpriteComp.Scale;
                        foreach (var protoLayer in protoSpriteComp.AllLayers)
                        {
                            protoScale *= protoLayer.Scale;
                            break;
                        }
                    }

                    protoData = (_sprite.Frame0(meta.EntityPrototype), protoScale);
                    _protoTextureCache[meta.EntityPrototype.ID] = protoData;
                }

                SetExtraLayerCount(uid, sprite, comp, 0);
                _sprite.LayerSetTexture((uid, sprite), layerIndex, protoData.Texture);
                _sprite.LayerSetScale((uid, sprite), layerIndex, protoData.Scale);
                _sprite.LayerSetOffset((uid, sprite), layerIndex, Vector2.Zero);
                _sprite.LayerSetColor((uid, sprite), layerIndex, Color.White);
                continue;
            }

            // Grow/shrink our own extra layers so the sprite can fit every item layer.
            // Extras are appended at the end of the sprite and drawn on top of the mapped layer.
            SetExtraLayerCount(uid, sprite, comp, usable.Count - 1);
            var baseCount = sprite.AllLayers.Count() - comp.ExtraLayers;

            // Copy every item layer, preserving order (bowl first, contents above).
            for (var i = 0; i < usable.Count; i++)
            {
                var (src, tex) = usable[i];
                var dst = i == 0 ? layerIndex : baseCount + (i - 1);

                _sprite.LayerSetTexture((uid, sprite), dst, tex);
                _sprite.LayerSetScale((uid, sprite), dst, itemSprite!.Scale * src.Scale);
                _sprite.LayerSetOffset((uid, sprite), dst, src.Offset);
                _sprite.LayerSetColor((uid, sprite), dst, src.Color);
            }
        }
    }

    private void ClearVisuals(EntityUid uid, SpriteComponent sprite, ItemSlotRendererComponent comp, int layerIndex)
    {
        SetExtraLayerCount(uid, sprite, comp, 0);

        if (!sprite.TryGetLayer(layerIndex, out var layer) || layer.Texture == Texture.Transparent)
            return;

        _sprite.LayerSetTexture((uid, sprite), layerIndex, Texture.Transparent);
        _sprite.LayerSetScale((uid, sprite), layerIndex, Vector2.One);
        _sprite.LayerSetOffset((uid, sprite), layerIndex, Vector2.Zero);
        _sprite.LayerSetColor((uid, sprite), layerIndex, Color.White);
    }

    /// <summary>
    /// Adjusts the number of extra layers this renderer has appended (always at the end of the
    /// sprite) to <paramref name="count"/>, without disturbing the entity's own base layers.
    /// </summary>
    private void SetExtraLayerCount(EntityUid uid, SpriteComponent sprite, ItemSlotRendererComponent comp, int count)
    {
        while (comp.ExtraLayers < count)
        {
            _sprite.AddBlankLayer((uid, sprite));
            comp.ExtraLayers++;
        }

        while (comp.ExtraLayers > count)
        {
            _sprite.RemoveLayer((uid, sprite), sprite.AllLayers.Count() - 1);
            comp.ExtraLayers--;
        }
    }
}
