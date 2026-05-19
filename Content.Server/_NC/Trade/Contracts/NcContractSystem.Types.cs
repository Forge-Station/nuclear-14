using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private readonly record struct ClaimTakeEntry(
        EntityUid Root,
        EntityUid Entity,
        int Amount,
        bool IsStack,
        string TargetItem,
        PrototypeMatchMode MatchMode);

    private enum ClaimFailureReason : byte
    {
        None = 0,
        StoreMissing,
        ContractMissing,
        NotTaken,
        NoValidTargets,
        InvalidTarget,
        NotEnoughItems,
        MissingCrate,
        MissingBody,
        MissingProof,
        ObjectiveNotCompleted,
        ObjectiveFailed,
        ExecutionFailed,
    }

    private readonly record struct ClaimAttemptResult(bool Success, ClaimFailureReason Reason, string? Details)
    {
        public static ClaimAttemptResult Ok() => new(true, ClaimFailureReason.None, null);
        public static ClaimAttemptResult Fail(ClaimFailureReason reason, string? details = null) => new(false, reason, details);
    }

    private readonly record struct PoolEntry(ContractRewardDef Def, string Key);

    private enum QuasiKeyKind : byte
    {
        Req,
        Tc,
        TReq,
        RAmount
    }

    private enum ContractPoolCandidateKind : byte
    {
        Supply = 1,
        Retrieval = 2,
        Hunt = 3,
        GhostRole = 4
    }

    private sealed class ContractPoolCandidate
    {
        public ContractPoolCandidateKind Kind;
        public string Id = string.Empty;
        public bool Repeatable = true;
        public int Weight;
        public string OfferPoolId = string.Empty;
        public string OfferPoolName = string.Empty;
        public int OfferPoolOrder = int.MaxValue;
        public string OfferPoolColor = string.Empty;
        public NcSupplyContractPrototype? Supply;
        public NcRetrievalContractPrototype? Retrieval;
        public NcHuntContractPrototype? Hunt;
        public NcGhostRoleContractPrototype? GhostRole;
    }

    private readonly record struct QuasiKey(QuasiKeyKind Kind, EntityUid Store, string ProtoId, string? Extra);
}

[ByRefEvent]
public readonly record struct NcContractsChangedEvent;
