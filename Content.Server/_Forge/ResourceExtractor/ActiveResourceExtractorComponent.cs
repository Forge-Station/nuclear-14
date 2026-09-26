using Robust.Shared.Prototypes;

namespace Content.Server._Forge.ResourceExtractor;

/// <summary>
/// Present only while the extractor motor is doing paid work.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveResourceExtractorComponent : Component;

/// <summary>
/// Owns a completed, fuel-paid cycle while it waits for the globally budgeted
/// output transaction. Keeping the batch server-side prevents prototype data
/// and runtime state from being mixed in the shared machine component.
/// </summary>
[RegisterComponent]
public sealed partial class PendingResourceExtractorOperationComponent : Component
{
    public readonly List<EntProtoId> Batch = new();

    /// <summary>
    /// Avoids retrying an expensive insertion every tick while the hopper is
    /// full. Capacity changes are detected by a bounded periodic retry.
    /// </summary>
    public bool WaitingForCapacity;

    public TimeSpan NextCapacityRetry;
}
