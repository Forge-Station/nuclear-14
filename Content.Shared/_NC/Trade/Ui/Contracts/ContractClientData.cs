using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public sealed class ContractClientData
{
    public bool Completed;
    public string Description = string.Empty;
    public ContractFlowStatus FlowStatus;
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Progress;
    public string OfferPoolId = string.Empty;
    public string OfferPoolName = string.Empty;
    public int OfferPoolOrder = int.MaxValue;
    public string OfferPoolColor = string.Empty;

    public bool Repeatable;
    public bool Taken;
    public bool SupportsPinpointer;
    public ContractExecutionKind ExecutionKind = ContractExecutionKind.InventoryDelivery;
    public ContractRuntimeContextData Runtime = new();
    public int Required;
    public List<ContractRewardData> Rewards = new();

    public string TargetItem = string.Empty;
    public string TurnInItem = string.Empty;
    public string SourceHint = string.Empty;
    public string DestinationHint = string.Empty;
    public bool IsRetrievalRoute;
    public NcRetrievalClaimMode RetrievalClaimMode;
    public bool RetrievalProofIsBearer;
    public NcHuntCompletionMode HuntCompletionMode = NcHuntCompletionMode.TrophyTurnIn;
    public NcGhostRoleCompletionMode GhostRoleCompletionMode = NcGhostRoleCompletionMode.DeadBodyTurnIn;
    public List<ContractTargetClientData> Targets = new();

    public ContractClientData() { }

    public ContractClientData(
        string id,
        string name,
        string description,
        bool repeatable,
        bool taken,
        bool supportsPinpointer,
        ContractExecutionKind executionKind,
        ContractRuntimeContextData runtime,
        ContractFlowStatus flowStatus,
        bool completed,
        string targetItem,
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
        string offerPoolColor = "")
    {
        Id = id;
        Name = name;
        Description = description;
        Repeatable = repeatable;
        Taken = taken;
        SupportsPinpointer = supportsPinpointer;
        ExecutionKind = executionKind;
        Runtime = runtime;
        FlowStatus = flowStatus;
        Completed = completed;
        TargetItem = targetItem;
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

public static class ContractFingerprint
{
    private const int FnvOffset = unchecked((int) 2166136261u);
    private const int FnvPrime = 16777619;

    public static int ComputeFingerprint(this IReadOnlyList<ContractClientData> contracts)
    {
        unchecked
        {
            var h = FnvOffset;
            h = MixInt(h, contracts.Count);
            for (var i = 0; i < contracts.Count; i++)
                h = AppendContract(h, contracts[i]);
            return h;
        }
    }

    public static int ComputeFingerprint(this ContractClientData? contract)
    {
        return AppendContract(FnvOffset, contract);
    }

    private static int AppendContract(int seed, ContractClientData? contract)
    {
        unchecked
        {
            if (contract == null)
                return MixInt(seed, -1);

            var h = seed;
            h = MixString(h, contract.Id);
            h = MixString(h, contract.Name);
            h = MixString(h, contract.Description);
            h = MixString(h, contract.OfferPoolId);
            h = MixString(h, contract.OfferPoolName);
            h = MixInt(h, contract.OfferPoolOrder);
            h = MixString(h, contract.OfferPoolColor);
            h = MixString(h, contract.TargetItem);
            h = MixString(h, contract.TurnInItem);
            h = MixString(h, contract.SourceHint);
            h = MixString(h, contract.DestinationHint);
            h = MixBool(h, contract.IsRetrievalRoute);
            h = MixInt(h, (int) contract.RetrievalClaimMode);
            h = MixBool(h, contract.RetrievalProofIsBearer);
            h = MixInt(h, (int) contract.HuntCompletionMode);
            h = MixInt(h, (int) contract.GhostRoleCompletionMode);
            h = MixBool(h, contract.Repeatable);
            h = MixBool(h, contract.Taken);
            h = MixBool(h, contract.SupportsPinpointer);
            h = MixInt(h, (int) contract.ExecutionKind);
            h = MixInt(h, (int) contract.FlowStatus);
            h = MixBool(h, contract.Completed);
            h = MixInt(h, contract.Progress);
            h = MixInt(h, contract.Required);
            h = AppendRuntime(h, contract.Runtime);
            h = AppendTargets(h, contract.Targets);
            h = AppendRewards(h, contract.Rewards);
            return h;
        }
    }

    private static int AppendRuntime(int seed, ContractRuntimeContextData? runtime)
    {
        unchecked
        {
            if (runtime == null)
                return MixInt(seed, -1);

            var h = seed;
            h = MixInt(h, runtime.Stage);
            h = MixInt(h, runtime.StageGoal);
            h = MixInt(h, runtime.AcceptTimeoutRemainingSeconds);
            h = MixInt(h, runtime.GhostRoleSurvivalRemainingSeconds);
            h = MixBool(h, runtime.GhostRolePendingAcceptance);
            h = MixBool(h, runtime.Failed);
            h = MixString(h, runtime.FailureReason);
            h = MixString(h, runtime.StatusHint);
            return h;
        }
    }

    private static int AppendTargets(int seed, List<ContractTargetClientData>? targets)
    {
        unchecked
        {
            if (targets == null)
                return MixInt(seed, -1);

            var h = MixInt(seed, targets.Count);
            for (var i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                h = MixString(h, t.TargetItem);
                h = MixInt(h, t.Required);
                h = MixInt(h, t.Progress);
                h = MixInt(h, (int) t.MatchMode);
            }

            return h;
        }
    }

    private static int AppendRewards(int seed, List<ContractRewardData>? rewards)
    {
        unchecked
        {
            if (rewards == null)
                return MixInt(seed, -1);

            var h = MixInt(seed, rewards.Count);
            for (var i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                h = MixInt(h, (int) r.Type);
                h = MixString(h, r.Id);
                h = MixInt(h, r.Amount);
            }

            return h;
        }
    }

    private static int MixString(int hash, string? value)
    {
        unchecked
        {
            if (value == null)
                return MixInt(hash, -1);

            var h = hash;
            for (var i = 0; i < value.Length; i++)
                h = (h ^ value[i]) * FnvPrime;

            return MixInt(h, value.Length);
        }
    }

    private static int MixInt(int hash, int value)
    {
        unchecked
        {
            var h = hash;
            h = (h ^ (value & 0xFF)) * FnvPrime;
            h = (h ^ ((value >> 8) & 0xFF)) * FnvPrime;
            h = (h ^ ((value >> 16) & 0xFF)) * FnvPrime;
            h = (h ^ ((value >> 24) & 0xFF)) * FnvPrime;
            return h;
        }
    }

    private static int MixBool(int hash, bool value) => MixInt(hash, value ? 1 : 0);
}
