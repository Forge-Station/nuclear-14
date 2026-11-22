using System.Linq; // Forge-Change: fire layers must remain above icon-smoothed structure layers.
using Content.Client.Atmos.Components;
using Content.Client.IconSmoothing; // Forge-Change
using Content.Shared.Atmos;
using Robust.Client.GameObjects;
using Robust.Shared.Map;

namespace Content.Client.Atmos.EntitySystems;

/// <summary>
/// This handles the display of fire effects on flammable entities.
/// </summary>
public sealed class FireVisualizerSystem : VisualizerSystem<FireVisualsComponent>
{
    [Dependency] private readonly PointLightSystem _lights = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FireVisualsComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<FireVisualsComponent, ComponentStartup>(OnComponentStartup, // Forge-Change
            after: [typeof(IconSmoothSystem)]);
        SubscribeLocalEvent<FireVisualsComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, FireVisualsComponent component, ComponentShutdown args)
    {
        if (component.LightEntity != null)
        {
            Del(component.LightEntity.Value);
            component.LightEntity = null;
        }

        // Need LayerMapTryGet because Init fails if there's no existing sprite / appearancecomp
        // which means in some setups (most frequently no AppearanceComp) the layer never exists.
        if (TryComp<SpriteComponent>(uid, out var sprite) &&
            sprite.LayerMapTryGet(FireVisualLayers.Fire, out var layer))
        {
            sprite.RemoveLayer(layer);
        }
    }

    // Forge-Change-Start: keep fire above icon-smoothed structure layers.
    private void OnComponentInit(EntityUid uid, FireVisualsComponent component, ComponentInit args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || !HasComp<AppearanceComponent>(uid))
            return;

        EnsureFireLayer(component, sprite, false);
    }
    // Forge-Change-End

    // Forge-Add-Start
    private void OnComponentStartup(EntityUid uid, FireVisualsComponent component, ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || !TryComp(uid, out AppearanceComponent? appearance))
            return;

        // At startup the parent entity is fully initialized and icon smoothing has appended its layers,
        // so it is safe both to move fire to the top and to attach the client-side light entity.
        UpdateAppearance(uid, component, sprite, appearance);
    }
    // Forge-Add-End

    protected override void OnAppearanceChange(EntityUid uid, FireVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite != null)
            UpdateAppearance(uid, component, args.Sprite, args.Component);
    }

    private void UpdateAppearance(EntityUid uid, FireVisualsComponent component, SpriteComponent sprite, AppearanceComponent appearance)
    {
        AppearanceSystem.TryGetData<bool>(uid, FireVisuals.OnFire, out var onFire, appearance);
        AppearanceSystem.TryGetData<float>(uid, FireVisuals.FireStacks, out var fireStacks, appearance);

        // Forge-Change-Start
        // Icon smoothing and some other visualizers append their layers after component initialization.
        // Recreate the fire layer on ignition so that it cannot end up hidden behind a wall or door sprite.
        var index = EnsureFireLayer(component, sprite, onFire);
        // Forge-Change-End
        sprite.LayerSetVisible(index, onFire);

        if (!onFire)
        {
            if (component.LightEntity != null)
            {
                Del(component.LightEntity.Value);
                component.LightEntity = null;
            }

            return;
        }

        if (fireStacks > component.FireStackAlternateState && !string.IsNullOrEmpty(component.AlternateState))
            sprite.LayerSetState(index, component.AlternateState);
        else
            sprite.LayerSetState(index, component.NormalState);

        component.LightEntity ??= Spawn(null, new EntityCoordinates(uid, default));
        var light = EnsureComp<PointLightComponent>(component.LightEntity.Value);

        _lights.SetColor(component.LightEntity.Value, component.LightColor, light);

        // light needs a minimum radius to be visible at all, hence the + 1.5f
        _lights.SetRadius(component.LightEntity.Value, Math.Clamp(1.5f + component.LightRadiusPerStack * fireStacks, 0f, component.MaxLightRadius), light);
        _lights.SetEnergy(component.LightEntity.Value, Math.Clamp(1 + component.LightEnergyPerStack * fireStacks, 0f, component.MaxLightEnergy), light);

        // TODO flickering animation? Or just add a noise mask to the light? But that requires an engine PR.
    }

    // Forge-Add-Start: icon smoothing may append wall and door layers after component initialization.
    private static int EnsureFireLayer(FireVisualsComponent component, SpriteComponent sprite, bool moveToTop)
    {
        if (sprite.LayerMapTryGet(FireVisualLayers.Fire, out var index))
        {
            if (!moveToTop || index == sprite.AllLayers.Count() - 1)
                return index;

            sprite.RemoveLayer(index);
        }

        index = sprite.LayerMapReserveBlank(FireVisualLayers.Fire);
        sprite.LayerSetVisible(index, false);
        sprite.LayerSetShader(index, "unshaded");
        if (component.Sprite != null)
            sprite.LayerSetRSI(index, component.Sprite);

        return index;
    }
    // Forge-Add-End
}

public enum FireVisualLayers : byte
{
    Fire
}
