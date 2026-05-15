using Robust.Shared.Serialization;
using Content.Shared.Customization.Systems;

namespace Content.Shared._NC.Trade;

[Serializable]
public sealed class ContractObjectiveConfigData
{
    public int AcceptTimeoutSeconds;

    public ContractPointSelectorPrototype? SpawnPoint { get; set; }
    public ContractPointSelectorPrototype? DropoffPoint { get; set; }
    public string TargetPrototype { get; set; } = string.Empty;
    public string DeliverySpawnPrototype { get; set; } = string.Empty;
    public string StructurePrototype { get; set; } = string.Empty;
    public string GhostRole { get; set; } = string.Empty;
    public string ProofPrototype { get; set; } = string.Empty;
    public string GhostRolePrototype { get; set; } = string.Empty;
    public string GhostRoleName { get; set; } = string.Empty;
    public string GhostRoleDescription { get; set; } = string.Empty;
    public string GhostRoleRules { get; set; } = string.Empty;
    public List<CharacterRequirement> GhostRoleRequirements { get; set; } = new();
    public bool PreserveTargetOnComplete;
    public bool AllowStoreWorldTurnIn;

    public bool GivePinpointer = true;
    public string PinpointerPrototype { get; set; } = string.Empty;

    public string GuardPrototype { get; set; } = string.Empty;
    public int GuardCount;

    public string RepairToolQuality { get; set; } = string.Empty;
    public float RepairDoAfterSeconds;
    public string RepairStageSound { get; set; } = string.Empty;

    // Phase M: see StoreContractRuntimePrototype for semantics. These are copied from the
    // prototype at contract-creation time so the runtime doesn't have to re-resolve the proto.
    public bool SpawnItems;
    public List<string> SpawnSpecific { get; set; } = new();

    // Retrieval V2 Route: copied from ncRetrievalRoutePreset at contract generation time.
    public string RetrievalRouteId { get; set; } = string.Empty;
    public bool RetrievalSpawnEnabled;
    public ContractPointSelectorPrototype? RetrievalSpawnPoint { get; set; }
    public bool RetrievalSpawnFallbackToStore;
    public bool RetrievalRequireSpawnedEntities;

    public NcRetrievalDestinationTargetType RetrievalDestinationType;
    public string RetrievalDestinationId { get; set; } = string.Empty;
    public ContractPointSelectorPrototype? RetrievalDestinationPoint { get; set; }
    public float RetrievalDestinationRadius;
    public bool RetrievalConsumeCargo;
    public bool RetrievalLockDeliveredCargo;

    public bool RetrievalProofEnabled;
    public bool RetrievalProofConsumeOnRewardClaim;
    public NcRetrievalProofOwnership RetrievalProofOwnership;
    public NcRetrievalProofReissuePolicy RetrievalProofReissue;

    public bool RetrievalGuidancePinpointerEnabled;
    public NcRetrievalPinpointerTargetMode RetrievalGuidancePinpointerTarget;
    public string RetrievalGuidancePinpointerPrototype { get; set; } = string.Empty;
    public int RetrievalGuidanceMaxActivePinpointers;
    public string RetrievalSourceHint { get; set; } = string.Empty;
    public string RetrievalDestinationHint { get; set; } = string.Empty;
}
