using System.Numerics;
using Content.Client.Parallax;
using Content.Client.Weather;
using Content.Shared._NC.Clouds; // NC - Clouds
using Content.Shared.Salvage;
using Content.Shared.Weather;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Overlays;

/// <summary>
/// Simple re-useable overlay with stencilled texture.
/// </summary>
public sealed partial class StencilOverlay : Overlay
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    private readonly ParallaxSystem _parallax;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;
    private readonly WeatherSystem _weather;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private IRenderTexture? _blep;

    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _weatherDrawShader;

    // Stencil mask throttle: while the view is static the roofed-tile
    // stencil mask only needs rebuilding at 4 Hz to catch tile/roof changes, instead
    // of every frame. Any view change rebuilds it, since the mask is screen space.
    private TimeSpan _nextStencilUpdate;
    private Matrix3x2 _lastStencilMatrix;
    private MapId? _lastStencilMap;

    public StencilOverlay(ParallaxSystem parallax, SharedTransformSystem transform, SpriteSystem sprite, WeatherSystem weather)
    {
        ZIndex = ParallaxSystem.ParallaxZIndex + 1;
        _parallax = parallax;
        _transform = transform;
        _sprite = sprite;
        _weather = weather;
        IoCManager.InjectDependencies(this);
        _shader = _protoManager.Index<ShaderPrototype>("WorldGradientCircle").InstanceUnique();
        _weatherDrawShader = _protoManager.Index<ShaderPrototype>("WeatherDraw").InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mapUid = _mapManager.GetMapEntityId(args.MapId);
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();

        if (_blep?.Texture.Size != args.Viewport.Size)
        {
            _blep?.Dispose();
            _blep = _clyde.CreateRenderTarget(args.Viewport.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "weather-stencil");
            _lastStencilMap = null;
        }

        if (_entManager.TryGetComponent<WeatherComponent>(mapUid, out var comp))
        {
            foreach (var (proto, weather) in comp.Weather)
            {
                if (!_protoManager.TryIndex<WeatherPrototype>(proto, out var weatherProto))
                    continue;
                if (weatherProto.Sprite == null)
                    continue;
                var alpha = _weather.GetPercent(weather, mapUid);
                DrawWeather(args, weatherProto, alpha, invMatrix);
            }
        }

        // NC - Clouds
        if (_entManager.TryGetComponent<NCCloudLayerComponent>(mapUid, out var cloudLayer))
        {
            DrawCloudLayer(args, cloudLayer, invMatrix);
            // Other overlays share this render target and can overwrite the weather mask.
            _lastStencilMap = null;
        }
        // NC - Clouds

        if (_entManager.TryGetComponent<RestrictedRangeComponent>(mapUid, out var restrictedRangeComponent))
        {
            DrawRestrictedRange(args, restrictedRangeComponent, invMatrix);
            _lastStencilMap = null;
        }

        args.WorldHandle.UseShader(null);
        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }
}
