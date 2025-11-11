using System.Numerics;
using Content.Shared._NC.Clouds;
using Content.Shared.Light.Components;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private void DrawCloudLayer(in OverlayDrawArgs args, NCCloudLayerComponent component, Matrix3x2 invMatrix)
    {
        if (!component.IsActive || component.CurrentOpacity <= 0f)
            return;

        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;
        var worldAABB = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var eyePosition = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;

        worldHandle.RenderInRenderTarget(_blep!, () =>
        {
            var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
            _grids.Clear();
            _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

            foreach (var grid in _grids)
            {
                var worldMatrix = _transform.GetWorldMatrix(grid, xformQuery) * invMatrix;
                worldHandle.SetTransform(worldMatrix);
                _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);

                foreach (var tile in grid.Comp.GetTilesIntersecting(worldAABB))
                {
                    if (component.RespectWeatherBlockers && _weather.CanWeatherAffect(grid.Owner, grid, tile, roofComp))
                        continue;

                    var tileBox = new Box2(tile.GridIndices * grid.Comp.TileSize,
                        (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);
                    worldHandle.DrawRect(tileBox, Color.White);
                }
            }
        }, Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);
        if (!_protoManager.TryIndex<ShaderPrototype>(component.MaskShaderPrototype, out var maskPrototype))
            return;

        var maskShader = maskPrototype.Instance();
        worldHandle.UseShader(maskShader);
        worldHandle.DrawTextureRect(_blep!.Texture, worldBounds);

        var curTime = _timing.RealTime;
        var sprite = _sprite.GetFrame(new SpriteSpecifier.Texture(component.TexturePath), curTime);

        if (!_protoManager.TryIndex<ShaderPrototype>(component.DrawShaderPrototype, out var drawPrototype))
            return;

        var drawShader = drawPrototype.Instance();
        worldHandle.UseShader(drawShader);
        var alpha = MathHelper.Clamp(component.Opacity * component.CurrentOpacity, 0f, 1f);
        var modulate = component.Tint.WithAlpha(alpha);
        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, eyePosition, component.DriftPerSecond,
            scale: component.Scale, modulate: modulate);

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
