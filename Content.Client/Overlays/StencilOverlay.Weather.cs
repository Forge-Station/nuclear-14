using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Client.Graphics;
/// Forge-Change
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
/// Forge-Change-Del using Robust.Shared.Physics.Components;

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
        /// Forge-Change-Del var worldBounds = args.WorldBounds;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        /// Forge-Change-Start
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
        /// Forge-Change-End

        // Cut out the irrelevant bits via stencil
        // This is why we don't just use parallax; we might want specific tiles to get drawn over
        // particularly for planet maps or stations.
        /// Forge-Change-Del worldHandle.RenderInRenderTarget(_blep!, () =>
        /// Forge-Change
        if (rebuildStencil)
        {
            /// Forge-Change-Del var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
            /// Forge-Change-Del _grids.Clear();
            /// Forge-Change-Start
            _nextStencilUpdate = _timing.RealTime + TimeSpan.FromSeconds(0.25);
            _lastStencilMatrix = invMatrix;
            _lastStencilMap = mapId;
            /// Forge-Change-End

            /// Forge-Change-Del // idk if this is safe to cache in a field and clear sloth help
            /// Forge-Change-Del _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);
            /// Forge-Change-Start
            worldHandle.RenderInRenderTarget(_blep!, () =>
            {
                var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
                _grids.Clear();
            /// Forge-Change-End

            /// Forge-Change-Del foreach (var grid in _grids)
            /// Forge-Change-Del {
                /// Forge-Change-Del var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                /// Forge-Change-Del var matty =  Matrix3x2.Multiply(matrix, invMatrix);
                /// Forge-Change-Del worldHandle.SetTransform(matty);
                /// Forge-Change-Del _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);
                /// Forge-Change
                _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

                /// Forge-Change-Del foreach (var tile in grid.Comp.GetTilesIntersecting(worldAABB))
                /// Forge-Change
                foreach (var grid in _grids)
                {
                    /// Forge-Change-Del // Ignored tiles for stencil
                    /// Forge-Change-Del if (_weather.CanWeatherAffect(grid.Owner, grid, tile, roofComp))
                    /// Forge-Change-Start
                    var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                    var matty = Matrix3x2.Multiply(matrix, invMatrix);
                    worldHandle.SetTransform(matty);
                    _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);

                    foreach (var tile in _entManager.System<SharedMapSystem>().GetTilesIntersecting(grid.Owner, grid.Comp, worldAABB))
                    /// Forge-Change-End
                    {
                        /// Forge-Change-Del continue;
                        /// Forge-Change-Start
                        // Ignored tiles for stencil
                        if (_weather.CanWeatherAffect(grid.Owner, grid, tile, roofComp))
                        {
                            continue;
                        }

                        var gridTile = new Box2(tile.GridIndices * grid.Comp.TileSize,
                            (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);

                        worldHandle.DrawRect(gridTile, Color.White);
                        /// Forge-Change-End
                    }
                    /// Forge-Change-Del var gridTile = new Box2(tile.GridIndices * grid.Comp.TileSize,
                        /// Forge-Change-Del (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);
                    /// Forge-Change-Del worldHandle.DrawRect(gridTile, Color.White);
                }
            /// Forge-Change-Del }
        /// Forge-Change-Del }, Color.Transparent);
            /// Forge-Change-Start
            }, Color.Transparent);
        }
            /// Forge-Change-End

        worldHandle.SetTransform(Matrix3x2.Identity);
        /// Forge-Change-Del worldHandle.UseShader(_protoManager.Index<ShaderPrototype>("StencilMask").Instance());
        /// Forge-Change-Del worldHandle.DrawTextureRect(_blep!.Texture, worldBounds);
        var curTime = _timing.RealTime;
        var sprite = _sprite.GetFrame(weatherProto.Sprite, curTime);

        /// Forge-Change-Del // Draw the rain
        /// Forge-Change-Del worldHandle.UseShader(_protoManager.Index<ShaderPrototype>("StencilDraw").Instance());
        /// Forge-Change-Start
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

        /// Forge-Change-End
        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, position, Vector2.Zero, modulate: (weatherProto.Color ?? Color.White).WithAlpha(alpha));

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
