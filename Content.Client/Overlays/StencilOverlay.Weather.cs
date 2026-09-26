using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private List<Entity<MapGridComponent>> _grids = new();

    private void DrawWeather(in OverlayDrawArgs args, WeatherPrototype weatherProto, float alpha, Matrix3x2 invMatrix)
    {
        if (weatherProto.Sprite == null)
            return;
        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;
        var worldAABB = args.WorldAABB;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var viewport = args.Viewport;
        var renderScale = viewport.RenderScale.X;
        var viewportSize = viewport.Size;
        var hasEye = viewport.Eye != null;
        var eyePosition = viewport.Eye?.Position.Position ?? Vector2.Zero;
        var eyeZoom = viewport.Eye?.Zoom ?? Vector2.One;

        // Throttle stencil mask rebuild to 4 Hz. The mask is baked in
        // screen space, so it is only reusable while the view is completely static;
        // invMatrix covers eye position, zoom and rotation. Otherwise the timer picks
        // up tile/roof changes without rebuilding the mask every frame.
        var rebuildStencil = _timing.RealTime >= _nextStencilUpdate ||
            invMatrix != _lastStencilMatrix || _lastStencilMap != mapId;

        // Cut out the irrelevant bits via stencil
        // This is why we don't just use parallax; we might want specific tiles to get drawn over
        // particularly for planet maps or stations.
        if (rebuildStencil)
        {
            _nextStencilUpdate = _timing.RealTime + TimeSpan.FromSeconds(0.25);
            _lastStencilMatrix = invMatrix;
            _lastStencilMap = mapId;

            worldHandle.RenderInRenderTarget(_blep!, () =>
            {
                var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
                _grids.Clear();

                _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

                foreach (var grid in _grids)
                {
                    var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                    var matty = Matrix3x2.Multiply(matrix, invMatrix);
                    worldHandle.SetTransform(matty);
                    _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);

                    foreach (var tile in _entManager.System<SharedMapSystem>().GetTilesIntersecting(grid.Owner, grid.Comp, worldAABB))
                    {
                        // Ignored tiles for stencil
                        if (_weather.CanWeatherAffect(grid.Owner, grid, tile, roofComp))
                        {
                            continue;
                        }

                        var gridTile = new Box2(tile.GridIndices * grid.Comp.TileSize,
                            (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);

                        worldHandle.DrawRect(gridTile, Color.White);
                    }
                }
            }, Color.Transparent);
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        var curTime = _timing.RealTime;
        var sprite = _sprite.GetFrame(weatherProto.Sprite, curTime);

        _weatherDrawShader.SetParameter("MASK_TEXTURE", _blep!.Texture);

        if (weatherProto.VisibilityClearRadius > 0f && hasEye)
        {
            var length = eyeZoom.X;
            var pixelCenter = Vector2.Transform(eyePosition, invMatrix);
            var pixelMaxRange = weatherProto.VisibilityClearRadius * renderScale / length * EyeManager.PixelsPerMeter;
            var pixelBufferRange = MathF.Max(1f, weatherProto.VisibilityClearBuffer * renderScale / length * EyeManager.PixelsPerMeter);
            var pixelMinRange = MathF.Max(0f, pixelMaxRange - pixelBufferRange);

            _weatherDrawShader.SetParameter("position", new Vector2(pixelCenter.X, viewportSize.Y - pixelCenter.Y));
            _weatherDrawShader.SetParameter("maxRange", pixelMaxRange);
            _weatherDrawShader.SetParameter("minRange", pixelMinRange);
            _weatherDrawShader.SetParameter("bufferRange", pixelBufferRange);
        }
        else
        {
            _weatherDrawShader.SetParameter("maxRange", 0f);
            _weatherDrawShader.SetParameter("minRange", 0f);
            _weatherDrawShader.SetParameter("bufferRange", 1f);
        }

        _weatherDrawShader.SetParameter("gradient", 0.80f);
        worldHandle.UseShader(_weatherDrawShader);

        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, position, Vector2.Zero, modulate: (weatherProto.Color ?? Color.White).WithAlpha(alpha));

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
