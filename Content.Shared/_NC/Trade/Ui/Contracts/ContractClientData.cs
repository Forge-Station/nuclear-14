using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class ContractClientData
{
    public bool Completed;
    public string Description = string.Empty;
    public string DestinationHint = string.Empty;
    public ContractExecutionKind ExecutionKind = ContractExecutionKind.InventoryDelivery;
    public ContractFlowStatus FlowStatus;
    public NcGhostRoleCompletionMode GhostRoleCompletionMode = NcGhostRoleCompletionMode.DeadBodyTurnIn;
    public NcHuntCompletionMode HuntCompletionMode = NcHuntCompletionMode.TrophyTurnIn;
    public string Id = string.Empty;
    public bool IsRetrievalRoute;
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;
    public string Name = string.Empty;
    public string OfferPoolColor = string.Empty;
    public string OfferPoolId = string.Empty;
    public string OfferPoolName = string.Empty;
    public int OfferPoolOrder = int.MaxValue;
    public bool PartialTurnInAvailable;
    public int Progress;

    public bool Repeatable;
    public int Required;
    public NcRetrievalClaimMode RetrievalClaimMode;
    public bool RetrievalProofIsBearer;
    public List<ContractRewardData> Rewards = new();
    public ContractRuntimeContextData Runtime = new();
    public string SourceHint = string.Empty;
    public bool SupportsPinpointer;
    public bool Taken;

    public string TargetItem = string.Empty;
    public List<ContractTargetClientData> Targets = new();
    public string TurnInItem = string.Empty;

    public ContractClientData() { }

    public ContractClientData(
        string id,
        string name,
        string description,
        bool repeatable,
        bool taken,
        bool supportsPinpointer,
        bool partialTurnInAvailable,
        ContractExecutionKind executionKind,
        ContractRuntimeContextData runtime,
        ContractFlowStatus flowStatus,
        bool completed,
        string targetItem,
        PrototypeMatchMode matchMode,
        string turnInItem,
        int required,
        int progress,
        List<ContractTargetClientData> targets,
        List<ContractRewardData> rewards,
        string sourceHint = "",
        string destinationHint = "",
        bool isRetrievalRoute = false,
        NcRetrievalClaimMode retrievalClaimMode = NcRetrievalClaimMode.StoreCargo,
        bool retrievalProofIsBearer = false,
        NcHuntCompletionMode huntCompletionMode = NcHuntCompletionMode.TrophyTurnIn,
        NcGhostRoleCompletionMode ghostRoleCompletionMode = NcGhostRoleCompletionMode.DeadBodyTurnIn,
        string offerPoolId = "",
        string offerPoolName = "",
        int offerPoolOrder = int.MaxValue,
        string offerPoolColor = ""
    )
    {
        Id = id;
        Name = name;
        Description = description;
        Repeatable = repeatable;
        Taken = taken;
        SupportsPinpointer = supportsPinpointer;
        PartialTurnInAvailable = partialTurnInAvailable;
        ExecutionKind = executionKind;
        Runtime = runtime;
        FlowStatus = flowStatus;
        Completed = completed;
        TargetItem = targetItem;
        MatchMode = matchMode;
        TurnInItem = turnInItem;
        Required = required;
        Progress = progress;
        OfferPoolId = offerPoolId;
        OfferPoolName = offerPoolName;
        OfferPoolOrder = offerPoolOrder;
        OfferPoolColor = offerPoolColor;
        Targets = targets;
        Rewards = rewards;
        SourceHint = sourceHint;
        DestinationHint = destinationHint;
        IsRetrievalRoute = isRetrievalRoute;
        RetrievalClaimMode = retrievalClaimMode;
        RetrievalProofIsBearer = retrievalProofIsBearer;
        HuntCompletionMode = huntCompletionMode;
        GhostRoleCompletionMode = ghostRoleCompletionMode;
    }
}
