using Content.Shared._Forge.Fire;
using Content.Shared.Smoking;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.Fire;

/// <summary>
/// Combines the reagent-derived color with concentration-derived opacity.
/// </summary>
public sealed class SurfaceVaporVisualizerSystem : VisualizerSystem<SurfaceVaporComponent>
{
    protected override void OnAppearanceChange(
        EntityUid uid,
        SurfaceVaporComponent component,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null ||
            !AppearanceSystem.TryGetData(uid, SmokeVisuals.Color, out Color color, args.Component) ||
            !AppearanceSystem.TryGetData(uid, SurfaceVaporVisuals.Stage, out SurfaceVaporStage stage, args.Component))
        {
            return;
        }

        var alpha = stage switch
        {
            SurfaceVaporStage.Weak => component.WeakAlpha,
            SurfaceVaporStage.Normal => component.NormalAlpha,
            SurfaceVaporStage.Dense => component.DenseAlpha,
            _ => component.WeakAlpha,
        };
        args.Sprite.Color = color.WithAlpha(Math.Clamp(alpha, 0f, 1f));
    }
}
