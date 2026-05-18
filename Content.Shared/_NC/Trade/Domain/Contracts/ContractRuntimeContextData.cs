using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public sealed class ContractRuntimeContextData
{
    public int Stage;
    public int StageGoal = 1;
    public int AcceptTimeoutRemainingSeconds;
    public int GhostRoleSurvivalRemainingSeconds;
    public bool GhostRolePendingAcceptance;
    public bool Failed;

    public string FailureReason = string.Empty;
    public string StatusHint = string.Empty;
}
