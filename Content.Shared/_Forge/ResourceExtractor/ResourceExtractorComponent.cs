using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.ResourceExtractor;

/// <summary>
/// A fuel-powered machine that accumulates paid working time and periodically
/// places a bounded batch of resources into an internal output container.
/// </summary>
[RegisterComponent, Access(typeof(SharedResourceExtractorSystem))]
public sealed partial class ResourceExtractorComponent : Component
{
    public const string OutputContainerId = "storagebase";

    [DataField(required: true)]
    public ProtoId<ResourceExtractorProductionPrototype> Production;

    [DataField]
    public TimeSpan ExtractionDuration = TimeSpan.FromMinutes(4);

    /// <summary>
    /// Effective fuel units consumed per second. These are the same units used
    /// by GeneratorGetFuelEvent and GeneratorUseFuel.
    /// </summary>
    [DataField]
    public float FuelConsumptionRate = 0.11111111f;

    /// <summary>
    /// The user's run latch. Fuel exhaustion clears it; output blockage does not.
    /// </summary>
    public bool UserEnabled;

    /// <summary>
    /// Work time that has actually been paid for with fuel.
    /// </summary>
    public TimeSpan ExtractionProgress;

    public ResourceExtractorStatus Status = ResourceExtractorStatus.Off;

    [ViewVariables]
    public float UiUpdateAccumulator;
}

[Serializable, NetSerializable]
public enum ResourceExtractorStatus : byte
{
    Off,
    Running,
    NoFuel,
    OutputFull,
    Clogged,
    Unanchored,
    InvalidConfiguration,
    ProcessingOutput,
}

[Serializable, NetSerializable]
public enum ResourceExtractorVisuals : byte
{
    Running,
}
