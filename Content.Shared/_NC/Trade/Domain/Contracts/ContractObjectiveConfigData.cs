using Content.Shared.Customization.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Enums;


namespace Content.Shared._NC.Trade;


[Serializable]
public sealed class ContractObjectiveConfigData
{
    public int AcceptTimeoutSeconds;
    public bool AllowStoreWorldTurnIn;
    public int? GhostRoleCharacterAge;
    public Color? GhostRoleCharacterFacialHairColor;
    public Gender? GhostRoleCharacterGender;
    public Color? GhostRoleCharacterHairColor;
    public Sex? GhostRoleCharacterSex;
    public Color? GhostRoleCharacterSkinColor;
    public NcGhostRoleCompletionMode GhostRoleCompletionMode = NcGhostRoleCompletionMode.DeadBodyTurnIn;
    public int GhostRoleSurvivalDurationSeconds;

    public bool GivePinpointer = true;
    public int GuardCount;
    public NcHuntCompletionMode HuntCompletionMode = NcHuntCompletionMode.ConfirmedKill;

    // Spawned Hunt runtime metadata.
    public bool HuntEnabled;
    public bool PreserveTargetOnComplete;
    public NcRetrievalClaimMode RetrievalClaimMode;
    public bool RetrievalConsumeCargo;
    public float RetrievalDestinationRadius;

    public NcRetrievalDestinationTargetType RetrievalDestinationType;
    public int RetrievalGuidanceMaxActivePinpointers;

    public bool RetrievalGuidancePinpointerEnabled;
    public NcRetrievalPinpointerTargetMode RetrievalGuidancePinpointerTarget;
    public bool RetrievalLockDeliveredCargo;
    public bool RetrievalProofConsumeOnRewardClaim;

    public bool RetrievalProofEnabled;
    public NcRetrievalProofOwnership RetrievalProofOwnership;
    public NcRetrievalProofReissuePolicy RetrievalProofReissue;
    public bool RetrievalRequireSpawnedEntities;
    public bool RetrievalSpawnEnabled;
    public bool RetrievalSpawnFallbackToStore;

    // Inventory-delivery helper spawn metadata copied at contract creation time.
    public bool SpawnItems;

    public ContractPointSelectorPrototype? SpawnPoint { get; set; }
    public ContractPointSelectorPrototype? DropoffPoint { get; set; }
    public string TargetPrototype { get; set; } = string.Empty;
    public string DeliverySpawnPrototype { get; set; } = string.Empty;
    public string GhostRole { get; set; } = string.Empty;
    public string ProofPrototype { get; set; } = string.Empty;
    public string GhostRolePrototype { get; set; } = string.Empty;
    public string GhostRoleName { get; set; } = string.Empty;
    public string GhostRoleDescription { get; set; } = string.Empty;
    public string GhostRoleIcon { get; set; } = string.Empty;
    public string GhostRoleRules { get; set; } = string.Empty;
    public List<CharacterRequirement> GhostRoleRequirements { get; set; } = new();
    public string GhostRoleCharacterName { get; set; } = string.Empty;
    public string GhostRoleCharacterFacialHair { get; set; } = string.Empty;
    public string GhostRoleCharacterHair { get; set; } = string.Empty;
    public List<string> GhostRolePerks { get; set; } = new();
    public string GhostRoleSurvivalBriefing { get; set; } = string.Empty;
    public string GhostRoleSurvivalObjectiveTitle { get; set; } = string.Empty;
    public string GhostRoleSurvivalObjectiveDescription { get; set; } = string.Empty;
    public string PinpointerPrototype { get; set; } = string.Empty;

    public string GuardPrototype { get; set; } = string.Empty;
    public string HuntBodyPrototype { get; set; } = string.Empty;
    public List<string> SpawnSpecific { get; set; } = new();

    // Retrieval route: copied from ncRetrievalRoutePreset at contract generation time.
    public string RetrievalRouteId { get; set; } = string.Empty;
    public ContractPointSelectorPrototype? RetrievalSpawnPoint { get; set; }
    public string RetrievalDestinationId { get; set; } = string.Empty;
    public ContractPointSelectorPrototype? RetrievalDestinationPoint { get; set; }
    public string RetrievalGuidancePinpointerPrototype { get; set; } = string.Empty;
    public string RetrievalSourceHint { get; set; } = string.Empty;
    public string RetrievalDestinationHint { get; set; } = string.Empty;
}
