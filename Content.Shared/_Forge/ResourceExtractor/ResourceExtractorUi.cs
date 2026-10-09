using Robust.Shared.Serialization;

namespace Content.Shared._Forge.ResourceExtractor;

[Serializable, NetSerializable]
public enum ResourceExtractorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ResourceExtractorStartMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResourceExtractorStopMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResourceExtractorEjectFuelMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResourceExtractorOpenOutputMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResourceExtractorUiState(
    ResourceExtractorStatus status,
    bool userEnabled,
    float fuelFraction,
    float progressSeconds,
    float durationSeconds,
    int pendingUnits) : BoundUserInterfaceState
{
    public ResourceExtractorStatus Status { get; } = status;
    public bool UserEnabled { get; } = userEnabled;
    public float FuelFraction { get; } = fuelFraction;
    public float ProgressSeconds { get; } = progressSeconds;
    public float DurationSeconds { get; } = durationSeconds;
    public int PendingUnits { get; } = pendingUnits;
}
