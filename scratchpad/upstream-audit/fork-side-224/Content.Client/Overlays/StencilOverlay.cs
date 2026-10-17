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

    public StencilOverlay(ParallaxSystem parallax, SharedTransformSystem transform, SpriteSystem sprite, WeatherSystem weather)
    {
        ZIndex = ParallaxSystem.ParallaxZIndex + 1;
        _parallax = parallax;
        _transform = transform;
        _sprite = sprite;
        _weather = weather;
        IoCManager.InjectDependencies(this);
        _shader = _protoManager.Index<ShaderPrototype>("WorldGradientCircle").InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mapUid = _mapManager.GetMapEntityId(args.MapId);
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();

        if (_blep?.Texture.Size != args.Viewport.Size)
        {
            _blep?.Dispose();
            _blep = _clyde.CreateRenderTarget(args.Viewport.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "overlay-stencil"); // #Forge-Change
        }

        if (_entManager.TryGetComponent<WeatherComponent>(mapUid, out var comp))
        {
            var maskPrepared = false; // #Forge-Change: one mask preparation for all weather effects.
            foreach (var (proto, weather) in comp.Weather)
            {
                if (!_protoManager.TryIndex<WeatherPrototype>(proto, out var weatherProto))
                    continue;
                if (weatherProto.Sprite == null)
                    continue;
                // #Forge-Change-Start: prepare once per viewport draw, rather than once per effect.
                if (!maskPrepared)
                {
                    UpdateWeatherMask(args, invMatrix);
                    maskPrepared = true;
                }
                // #Forge-Change-End
                var alpha = _weather.GetPercent(weather, mapUid);
                DrawWeather(args, weatherProto, alpha); // #Forge-Change
            }
        }

        // NC - Clouds
        if (_entManager.TryGetComponent<NCCloudLayerComponent>(mapUid, out var cloudLayer))
        {
            DrawCloudLayer(args, cloudLayer, invMatrix);
        }
        // NC - Clouds

        if (_entManager.TryGetComponent<RestrictedRangeComponent>(mapUid, out var restrictedRangeComponent))
        {
            DrawRestrictedRange(args, restrictedRangeComponent, invMatrix);
        }

        args.WorldHandle.UseShader(null);
        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }

    // #Forge-Change-Start: release the cached weather mask and other owned rendering resources.
    protected override void DisposeBehavior()
    {
        _blep?.Dispose();
        _weatherMask?.Dispose();
        _shader.Dispose();
        base.DisposeBehavior();
    }
    // #Forge-Change-End
}
