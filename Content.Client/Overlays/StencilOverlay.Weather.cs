using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Shared.Map; // #Forge-Change
using Robust.Shared.Map.Components;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private List<Entity<MapGridComponent>> _grids = new();
    // #Forge-Change-Start: dedicated weather mask cache, independent of clouds and range overlays.
    private static readonly TimeSpan WeatherMaskInterval = TimeSpan.FromSeconds(0.25);
    private IRenderTexture? _weatherMask;
    private IClydeViewport? _weatherMaskViewport;
    private MapId _weatherMaskMap;
    private Matrix3x2 _weatherMaskMatrix;
    private TimeSpan _nextWeatherMaskUpdate;
    // #Forge-Change-End

    // #Forge-Change-Start: rebuild at 4 Hz for a static view, immediately when the view changes.
    private void UpdateWeatherMask(in OverlayDrawArgs args, Matrix3x2 invMatrix)
    {
        var resized = _weatherMask?.Texture.Size != args.Viewport.Size;
        if (resized)
        {
            _weatherMask?.Dispose();
            _weatherMask = _clyde.CreateRenderTarget(args.Viewport.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "weather-stencil");
        }

        // The mask is in screen space. Reuse it only for an unchanged view;
        // periodic rebuilds pick up roof, tile, blocker and grid changes.
        var now = _timing.RealTime;
        if (!resized && _weatherMaskViewport == args.Viewport && _weatherMaskMap == args.MapId
            && _weatherMaskMatrix == invMatrix && now < _nextWeatherMaskUpdate)
            return;

        _weatherMaskViewport = args.Viewport;
        _weatherMaskMap = args.MapId;
        _weatherMaskMatrix = invMatrix;
        _nextWeatherMaskUpdate = now + WeatherMaskInterval;

        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;
        var worldAABB = args.WorldAABB;

        // Cut out the irrelevant bits via stencil
        // This is why we don't just use parallax; we might want specific tiles to get drawn over
        // particularly for planet maps or stations.
        worldHandle.RenderInRenderTarget(_weatherMask!, () =>
        {
            var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
            _grids.Clear();

            // idk if this is safe to cache in a field and clear sloth help
            _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

            foreach (var grid in _grids)
            {
                var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                var matty =  Matrix3x2.Multiply(matrix, invMatrix);
                worldHandle.SetTransform(matty);
                _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);

                foreach (var tile in grid.Comp.GetTilesIntersecting(worldAABB))
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
    // #Forge-Change-End

    // #Forge-Change: draw each weather effect using the already prepared shared mask.
    private void DrawWeather(in OverlayDrawArgs args, WeatherPrototype weatherProto, float alpha)
    {
        if (weatherProto.Sprite == null)
            return;

        var worldHandle = args.WorldHandle;
        var worldAABB = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_protoManager.Index<ShaderPrototype>("StencilMask").Instance());
        worldHandle.DrawTextureRect(_weatherMask!.Texture, worldBounds); // #Forge-Change
        var curTime = _timing.RealTime;
        var sprite = _sprite.GetFrame(weatherProto.Sprite, curTime);

        // Draw the rain
        worldHandle.UseShader(_protoManager.Index<ShaderPrototype>("StencilDraw").Instance());
        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, position, Vector2.Zero, modulate: (weatherProto.Color ?? Color.White).WithAlpha(alpha));

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
