using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._NC.Trade;

[RegisterComponent]
public sealed partial class NcContractGhostRoleSurvivalObjectiveComponent : Component
{
    [DataField]
    public EntityUid Store;

    [DataField]
    public string ContractId = string.Empty;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan StartedAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan Deadline;

    [DataField]
    public bool Finished;

    [DataField]
    public bool Succeeded;
}
