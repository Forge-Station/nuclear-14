// Adapted from Misfit-Sanctuary/nuclear-14: client-only spent casing visuals.
// Forge adaptation: server-confirmed events, prototype sprites, 10-second lifetime and a 500-visual cap.
using System.Linq;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Audio;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._Forge.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly SpriteSystem _casingSprites = default!;

    [ValidatePrototypeId<EntityPrototype>]
    private const string SpentCartridgeVisual = "N14SpentCartridgeVisual";
    private const int MaxCasingVisuals = 500;
    private const float CasingOffset = 0.4f;
    private readonly Queue<EntityUid> _casingVisuals = new();

    private void OnSpentCartridge(SpentCartridgeEvent ev)
    {
        // The parent may have left PVS before this cosmetic event arrived.
        if (!TryGetEntity(ev.Coordinates.NetEntity, out var parent) || TerminatingOrDeleted(parent.Value))
            return;

        var coordinates = new EntityCoordinates(parent.Value, ev.Coordinates.Position);

        var visual = Spawn(SpentCartridgeVisual, coordinates.Offset(Random.NextVector2(CasingOffset)));
        TransformSystem.SetLocalRotation(visual, Random.NextAngle());

        // Read the already loaded prototype; never spawn a dummy cartridge to obtain its sprite.
        if (ProtoManager.TryIndex<EntityPrototype>(ev.Prototype, out var prototype))
        {
            if (prototype.TryGetComponent<SpriteComponent>(out var sprite))
            {
                _casingSprites.SetScale(visual, sprite.Scale);
                var layer = _casingSprites.LayerMapTryGet((sprite.Owner, sprite), AmmoVisualLayers.Base, out var index, false)
                    ? sprite[index]
                    : sprite.AllLayers.FirstOrDefault();
                var rsi = layer?.Rsi ?? sprite.BaseRSI;
                if (rsi != null)
                {
                    var state = "base-spent";
                    if (prototype.TryGetComponent<SpentAmmoVisualsComponent>(out var spent))
                        state = spent.Suffix ? $"{spent.State}-spent" : "spent";

                    // Some cartridges (e.g. grenades) have no dedicated spent sprite.
                    if (rsi.TryGetState(state, out _))
                        _casingSprites.LayerSetSprite(visual, 0, new SpriteSpecifier.Rsi(rsi.Path, state));
                    else if (layer?.RsiState.Name is { } baseState && rsi.TryGetState(baseState, out _))
                        _casingSprites.LayerSetSprite(visual, 0, new SpriteSpecifier.Rsi(rsi.Path, baseState));
                }
            }

            if (ev.PlaySound && prototype.TryGetComponent<CartridgeAmmoComponent>(out var cartridge))
            {
                Audio.PlayLocal(cartridge.EjectSound, visual, null, AudioParams.Default
                    .WithVariation(SharedContentAudioSystem.DefaultVariation).WithVolume(-1f));
            }
        }

        _casingVisuals.Enqueue(visual);
        while (_casingVisuals.Count > MaxCasingVisuals)
        {
            var oldest = _casingVisuals.Dequeue();
            if (Exists(oldest) && !TerminatingOrDeleted(oldest))
                QueueDel(oldest);
        }
    }
}
