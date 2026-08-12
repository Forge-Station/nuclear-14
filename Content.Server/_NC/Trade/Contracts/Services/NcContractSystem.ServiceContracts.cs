namespace Content.Server._NC.Trade;


internal interface IContractObjectiveRuntimeStore
{
    HashSet<(EntityUid Store, string ContractId)> ActiveGhostRoleObjectives { get; }
    HashSet<(EntityUid Store, string ContractId)> ActiveHuntObjectives { get; }
    HashSet<(EntityUid Store, string ContractId)> ActiveRetrievalRouteDeliveries { get; }
    HashSet<(EntityUid Store, string ContractId)> ActiveTrackedDeliveryDropoffObjectives { get; }
    Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByGuard { get; }
    Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByPinpointer { get; }
    Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByProof { get; }
    Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByRetrievalCargo { get; }
    Dictionary<(EntityUid Store, string ContractId), NcContractSystem.ObjectiveRuntimeState> ByContract { get; }
    Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByTarget { get; }
    List<(EntityUid Store, string ContractId)> KeysScratch { get; }
    bool IsEmpty { get; }

    void ClearSecondaryIndexesAndActiveSets();
}

internal interface IContractPinpointerRegistry
{
    List<EntityUid> ObjectivePinpointersScratch { get; }
    List<EntityUid> RetrievalPulledCargoScratch { get; }

    void RegisterIssuedPinpointer(
        IContractObjectiveRuntimeStore runtime,
        (EntityUid Store, string ContractId) key,
        NcContractSystem.ObjectiveRuntimeState state,
        EntityUid pinpointer
    );

    void UnregisterIssuedPinpointer(
        IContractObjectiveRuntimeStore runtime,
        EntityUid pinpointer,
        (EntityUid Store, string ContractId) key
    );
}
