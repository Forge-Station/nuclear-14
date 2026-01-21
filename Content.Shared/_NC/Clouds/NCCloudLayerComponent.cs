using System;
using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Shared._NC.Clouds;

/// <summary>
///     Adds a drifting cloud layer that is rendered on top of the map via the stencil overlay.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCCloudLayerComponent : Component
{
    /// <summary>
    ///     Direction and speed in world units per second for the cloud texture scroll.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 DriftPerSecond = new(0.25f, 0.0f);

    /// <summary>
    ///     Base speed used when choosing a random drift vector on the server.
    /// </summary>
    [DataField]
    public float DriftSpeed = 0.35f;

    /// <summary>
    ///     Additional random speed variance applied during initialization.
    /// </summary>
    [DataField]
    public float DriftSpeedVariance = 0.15f;

    /// <summary>
    ///     When true, the server will randomise the drift vector whenever a cloud event starts.
    /// </summary>
    [DataField]
    public bool RandomizeOnInit = true;

    /// <summary>
    ///     Alpha value applied to the texture draw call.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Opacity = 1;

    /// <summary>
    ///     Current fade multiplier replicated to clients. 0 = hidden, 1 = fully opaque.
    /// </summary>
    [AutoNetworkedField]
    public float CurrentOpacity;

    /// <summary>
    ///     Scale factor applied to the sprite when rendering.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Scale = 6f;

    /// <summary>
    ///     Tint colour of the clouds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color Tint = Color.White;

    /// <summary>
    ///     Resource path to the drifting texture.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ResPath TexturePath = new("/Textures/_Corvax/Parallaxes/Shadows.png");

    /// <summary>
    ///     Shader prototype used for the stencil masking step.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string MaskShaderPrototype = "StencilMask";

    /// <summary>
    ///     Shader prototype used when drawing the texture through the mask.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string DrawShaderPrototype = "StencilDraw";

    /// <summary>
    ///     Whether tiles blocked from weather effects should be skipped while building the stencil mask.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RespectWeatherBlockers = true;

    /// <summary>
    ///     Minimum length of a cloud event in seconds.
    /// </summary>
    [DataField]
    public float MinActiveDuration = 600f;

    /// <summary>
    ///     Maximum length of a cloud event in seconds.
    /// </summary>
    [DataField]
    public float MaxActiveDuration = 900f;

    /// <summary>
    ///     Minimum downtime between events in seconds.
    /// </summary>
    [DataField]
    public float MinDowntime = 300f;

    /// <summary>
    ///     Maximum downtime between events in seconds.
    /// </summary>
    [DataField]
    public float MaxDowntime = 1200f;

    /// <summary>
    ///     Seconds spent fading clouds in from transparent to opaque.
    /// </summary>
    [DataField]
    public float FadeInSeconds = 20f;

    /// <summary>
    ///     Seconds spent fading clouds back out once the event ends.
    /// </summary>
    [DataField]
    public float FadeOutSeconds = 20f;

    /// <summary>
    ///     If true, clouds will begin active as soon as the component spawns.
    /// </summary>
    [DataField]
    public bool StartActive;

    /// <summary>
    ///     True while the cloud event is currently active or fading.
    /// </summary>
    [AutoNetworkedField]
    public bool IsActive;

    /// <summary>
    ///     Server-side phase tracking for scheduling.
    /// </summary>
    public NCCloudLayerPhase Phase = NCCloudLayerPhase.Inactive;

    /// <summary>
    ///     If true the current event was manually triggered and ignores automatic scheduling until stopped.
    /// </summary>
    public bool ManualOverride;

    /// <summary>
    ///     Absolute time when a manual event should end, null for indefinite.
    /// </summary>
    public TimeSpan? ManualEndTime;

    /// <summary>
    ///     Absolute time when the active period should finish and the fade out begins.
    /// </summary>
    public TimeSpan? EventEndTime;

    /// <summary>
    ///     Next scheduled time for an automatic event.
    /// </summary>
    public TimeSpan? NextEventStart;
}

public enum NCCloudLayerPhase
{
    Inactive,
    FadingIn,
    Active,
    FadingOut,
}
