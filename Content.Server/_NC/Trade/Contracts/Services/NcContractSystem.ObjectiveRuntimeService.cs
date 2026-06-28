namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem
{
    private readonly IContractObjectiveRuntimeStore _objectiveRuntime = new ContractObjectiveRuntimeService();

    private sealed class ContractObjectiveRuntimeService : IContractObjectiveRuntimeStore
    {
        public HashSet<(EntityUid Store, string ContractId)> ActiveGhostRoleObjectives { get; } = new();
        public HashSet<(EntityUid Store, string ContractId)> ActiveHuntObjectives { get; } = new();
        public HashSet<(EntityUid Store, string ContractId)> ActiveRetrievalRouteDeliveries { get; } = new();
        public HashSet<(EntityUid Store, string ContractId)> ActiveTrackedDeliveryDropoffObjectives { get; } = new();
        public Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByGuard { get; } = new();
        public Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByPinpointer { get; } = new();
        public Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByProof { get; } = new();
        public Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByRetrievalCargo { get; } = new();
        public Dictionary<(EntityUid Store, string ContractId), ObjectiveRuntimeState> ByContract { get; } = new();
        public Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByTarget { get; } = new();
        public List<(EntityUid Store, string ContractId)> KeysScratch { get; } = new();
        public Dictionary<EntityUid, EntityUid> PinpointerOwners { get; } = new();

        public bool IsEmpty => ByContract.Count == 0;

        public void ClearSecondaryIndexesAndActiveSets()
        {
            ByTarget.Clear();
            ByPinpointer.Clear();
            PinpointerOwners.Clear();
            ByGuard.Clear();
            ByProof.Clear();
            ByRetrievalCargo.Clear();
            ActiveTrackedDeliveryDropoffObjectives.Clear();
            ActiveRetrievalRouteDeliveries.Clear();
            ActiveHuntObjectives.Clear();
            ActiveGhostRoleObjectives.Clear();
        }
    }
}
