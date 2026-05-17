using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

/// <summary>
/// Retrieval route source preset. Source owns only where cargo appears.
/// The actual cargo prototypes/counts live on ncRetrievalContract.cargo.
/// </summary>
[Prototype("ncRetrievalSourcePreset")]
public sealed partial class NcRetrievalSourcePresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>If true, cargo entries are spawned when the contract is taken.</summary>
    [DataField("spawnCargo")]
    public bool SpawnCargo { get; private set; } = true;

    /// <summary>Where cargo should be spawned. Store is rejected for route sources.</summary>
    [DataField("point", required: true)]
    public ContractPointSelectorPrototype Point { get; private set; } = new();

    /// <summary>Debug fallback only. Real content should keep this false.</summary>
    [DataField("fallbackToStore")]
    public bool FallbackToStore { get; private set; }
}

[Serializable, NetSerializable]
public enum NcRetrievalDestinationTargetType : byte
{
    StoreUi = 0,
    MarkerGroup = 1,
    ContainerGroup = 2
}

/// <summary>Destination target for route delivery.</summary>
[DataDefinition]
public sealed partial class NcRetrievalDestinationTargetData
{
    [DataField("type")]
    public NcRetrievalDestinationTargetType Type { get; set; } = NcRetrievalDestinationTargetType.StoreUi;

    /// <summary>Marker group id or NcContractTurnInContainer group id depending on Type.</summary>
    [DataField("id")]
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Retrieval route destination preset. The target type derives delivery behavior:
/// StoreUi = claim at trader, MarkerGroup = cargo in radius, ContainerGroup = cargo inside turn-in container.
/// </summary>
[Prototype("ncRetrievalDestinationPreset")]
public sealed partial class NcRetrievalDestinationPresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("target", required: true)]
    public NcRetrievalDestinationTargetData Target { get; private set; } = new();

    [DataField("radius")]
    public float Radius { get; private set; } = 2.0f;
}

[Serializable, NetSerializable]
public enum NcRetrievalClaimMode : byte
{
    /// <summary>
    /// Cargo is delivered directly to the store/store-owned turn-in target. Reward claim consumes only the delivered cargo.
    /// No proof item is involved.
    /// </summary>
    StoreCargo = 0,

    /// <summary>
    /// Cargo is delivered to a remote destination. The destination issues a physical proof item,
    /// and the proof must be brought back to the store to claim the reward.
    /// </summary>
    DestinationProof = 1
}

[Serializable, NetSerializable]
public enum NcRetrievalProofOwnership : byte
{
    /// <summary>Whoever physically brings the proof to the store may redeem it.</summary>
    Bearer = 0,

    /// <summary>Reserved for later. Not used by default because theft/trade of proofs is intended gameplay.</summary>
    ContractOwner = 1
}

[Serializable, NetSerializable]
public enum NcRetrievalProofReissuePolicy : byte
{
    Never = 0
}

/// <summary>Proof behavior preset. Proof is a transferable bearer receipt by default.</summary>
[Prototype("ncRetrievalProofPreset")]
public sealed partial class NcRetrievalProofPresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("prototype", required: true)]
    public string Prototype { get; private set; } = string.Empty;

    [DataField("ownership")]
    public NcRetrievalProofOwnership Ownership { get; private set; } = NcRetrievalProofOwnership.Bearer;

    [DataField("reissue")]
    public NcRetrievalProofReissuePolicy Reissue { get; private set; } = NcRetrievalProofReissuePolicy.Never;

    [DataField("consumeOnRewardClaim")]
    public bool ConsumeOnRewardClaim { get; private set; } = true;
}

[Serializable, NetSerializable]
public enum NcRetrievalPinpointerTargetMode : byte
{
    None = 0,
    CargoThenDestinationThenStore = 1
}

[DataDefinition]
public sealed partial class NcRetrievalPinpointerData
{
    [DataField("enabled")]
    public bool Enabled { get; set; }

    [DataField("target")]
    public NcRetrievalPinpointerTargetMode Target { get; set; } = NcRetrievalPinpointerTargetMode.None;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("maxActive")]
    public int MaxActive { get; set; } = 1;
}

/// <summary>Guidance preset. Guidance never controls spawn/claim; it only gives hints and pinpointer behavior.</summary>
[Prototype("ncRetrievalGuidancePreset")]
public sealed partial class NcRetrievalGuidancePresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("sourceHint")]
    public string SourceHint { get; private set; } = string.Empty;

    [DataField("destinationHint")]
    public string DestinationHint { get; private set; } = string.Empty;

    [DataField("pinpointer")]
    public NcRetrievalPinpointerData Pinpointer { get; private set; } = new();
}

[DataDefinition]
public sealed partial class NcRetrievalRouteDeliveryData
{
    [DataField("consumeCargo")]
    public bool ConsumeCargo { get; set; } = true;

    [DataField("lockDeliveredCargo")]
    public bool LockDeliveredCargo { get; set; } = false;
}

[DataDefinition]
public sealed partial class NcRetrievalRouteClaimData
{
    [DataField("mode")]
    public NcRetrievalClaimMode Mode { get; set; } = NcRetrievalClaimMode.StoreCargo;

    /// <summary>Required only for DestinationProof routes. StoreCargo routes must not define it.</summary>
    [DataField("proof")]
    public ProtoId<NcRetrievalProofPresetPrototype>? Proof { get; set; }
}

/// <summary>
/// Route preset composes repeated mechanics: source, destination, claim behavior, guidance and delivery flags.
/// Contracts keep only cargo + route + reward.
/// </summary>
[Prototype("ncRetrievalRoutePreset")]
public sealed partial class NcRetrievalRoutePresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("source")]
    public ProtoId<NcRetrievalSourcePresetPrototype>? Source { get; private set; }

    [DataField("destination", required: true)]
    public ProtoId<NcRetrievalDestinationPresetPrototype> Destination { get; private set; }

    [DataField("claim")]
    public NcRetrievalRouteClaimData Claim { get; private set; } = new();

    [DataField("guidance")]
    public ProtoId<NcRetrievalGuidancePresetPrototype>? Guidance { get; private set; }

    [DataField("delivery")]
    public NcRetrievalRouteDeliveryData Delivery { get; private set; } = new();
}

