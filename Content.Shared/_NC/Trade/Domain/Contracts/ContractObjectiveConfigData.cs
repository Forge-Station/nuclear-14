using Robust.Shared.Serialization;
using Content.Shared.Customization.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Enums;
using Robust.Shared.Maths;

namespace Content.Shared._NC.Trade;

[Serializable]
public sealed class ContractObjectiveConfigData
{
    public int AcceptTimeoutSeconds;

    public ContractPointSelectorPrototype? SpawnPoint { get; set; }
    public ContractPointSelectorPrototype? DropoffPoint { get; set; }
    public string TargetPrototype { get; set; } = string.Empty;
    public string DeliverySpawnPrototype { get; set; } = string.Empty;
    public string GhostRole { get; set; } = string.Empty;
    public string ProofPrototype { get; set; } = string.Empty;
    public string GhostRolePrototype { get; set; } = string.Empty;
    public string GhostRoleName { get; set; } = string.Empty;
    public string GhostRoleDescription { get; set; } = string.Empty;
    public string GhostRoleRules { get; set; } = string.Empty;
    public List<CharacterRequirement> GhostRoleRequirements { get; set; } = new();
    public string GhostRoleCharacterName { get; set; } = string.Empty;
    public Sex? GhostRoleCharacterSex;
    public Gender? GhostRoleCharacterGender;
    public int? GhostRoleCharacterAge;
    public string GhostRoleCharacterHair { get; set; } = string.Empty;
    public Color? GhostRoleCharacterHairColor;
    public Color? GhostRoleCharacterSkinColor;
    public List<string> GhostRolePerks { get; set; } = new();
    public NcGhostRoleCompletionMode GhostRoleCompletionMode = NcGhostRoleCompletionMode.DeadBodyTurnIn;
    public int GhostRoleSurvivalDurationSeconds;
    public string GhostRoleSurvivalBriefing { get; set; } = string.Empty;
    public string GhostRoleSurvivalObjectiveTitle { get; set; } = string.Empty;
    public string GhostRoleSurvivalObjectiveDescription { get; set; } = string.Empty;
    public bool PreserveTargetOnComplete;
    public bool AllowStoreWorldTurnIn;

    public bool GivePinpointer = true;
    public string PinpointerPrototype { get; set; } = string.Empty;

    public string GuardPrototype { get; set; } = string.Empty;
    public int GuardCount;

    // Spawned Hunt runtime metadata.
    public bool HuntEnabled;
    public NcHuntCompletionMode HuntCompletionMode = NcHuntCompletionMode.ConfirmedKill;
    public string HuntBodyPrototype { get; set; } = string.Empty;

    // Inventory-delivery helper spawn metadata copied at contract creation time.
    public bool SpawnItems;
    public List<string> SpawnSpecific { get; set; } = new();

    // Retrieval route: copied from ncRetrievalRoutePreset at contract generation time.
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
    public NcRetrievalClaimMode RetrievalClaimMode;

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
