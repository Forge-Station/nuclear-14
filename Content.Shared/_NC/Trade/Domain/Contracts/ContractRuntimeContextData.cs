using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class ContractRuntimeContextData
{
    public int AcceptTimeoutRemainingSeconds;
    public bool Failed;

    public string FailureReason = string.Empty;
    public bool GhostRolePendingAcceptance;
    public int GhostRoleSurvivalRemainingSeconds;
    public ContractObjectiveOutcome Outcome;
    public int Stage;
    public int StageGoal = 1;
    public string StatusHint = string.Empty;
}
