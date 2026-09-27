using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Fire;

/// <summary>
/// A reagent-bearing vapor cloud with concentration-driven visuals and occlusion.
/// Chemistry remains in the standard Smoke solution; this component owns its lifetime and density state.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurfaceVaporComponent : Component
{
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public float DecayPerSecond = 0.04f;

    [DataField]
    public FixedPoint2 MaxVolume = FixedPoint2.New(2);

    [DataField]
    public FixedPoint2 DeleteThreshold = FixedPoint2.New(0.03f);

    [DataField]
    public TimeSpan SupplyGracePeriod = TimeSpan.FromSeconds(2.5);

    [DataField]
    public float NormalThreshold = 0.15f;

    [DataField]
    public float DenseEnterThreshold = 1f;

    [DataField]
    public float DenseExitThreshold = 0.65f;

    [DataField]
    public float CardinalNeighborWeight = 0.25f;

    [DataField]
    public float DiagonalNeighborWeight = 0.1f;

    [DataField]
    public float ExposureDuration = 6f;

    [DataField]
    public float WeakAlpha = 0.25f;

    [DataField]
    public float NormalAlpha = 0.5f;

    [DataField]
    public float DenseAlpha = 0.85f;

    [ViewVariables]
    public TimeSpan NextUpdate;

    [ViewVariables]
    public TimeSpan LastSupplied;

    [ViewVariables]
    public bool Dense;

    [ViewVariables]
    public float EffectiveDensity;
}

[Serializable, NetSerializable]
public enum SurfaceVaporVisuals : byte
{
    Stage,
}

[Serializable, NetSerializable]
public enum SurfaceVaporStage : byte
{
    Weak,
    Normal,
    Dense,
}
