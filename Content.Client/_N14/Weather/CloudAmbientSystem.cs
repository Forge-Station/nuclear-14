using Content.Shared._NC.Clouds;
using Content.Shared._NC14.DayNightCycle;
using Robust.Shared.Map.Components;

namespace Content.Client._N14.Weather;

/// <summary>
/// Lerps <see cref="MapLightComponent.AmbientLightColor"/> toward
/// <see cref="NCCloudLayerComponent.OvercastAmbientTint"/> based on the current
/// cloud opacity, giving a visually darker overcast feeling during cloud events.
///
/// Runs after the day/night cycle has restored the base color. Maps without
/// that cycle are excluded to avoid repeatedly tinting an already tinted color.
/// </summary>
public sealed class CloudAmbientSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(DayNightCycleSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NCCloudLayerComponent, MapLightComponent, DayNightCycleComponent>();
        while (query.MoveNext(out _, out var clouds, out var mapLight, out _))
        {
            // No visible clouds — leave ambient color as-is.
            if (clouds.CurrentOpacity <= 0f)
                continue;

            var factor = Math.Clamp(clouds.CurrentOpacity * clouds.OvercastAmbientBlend, 0f, 1f);
            var current = mapLight.AmbientLightColor;
            var tint = clouds.OvercastAmbientTint;

            // Manual per-channel lerp (same pattern used by DayNightCycleSystem).
            mapLight.AmbientLightColor = new Color(
                current.R + (tint.R - current.R) * factor,
                current.G + (tint.G - current.G) * factor,
                current.B + (tint.B - current.B) * factor,
                current.A);
        }
    }
}
