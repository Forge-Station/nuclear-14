namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem
{
    private readonly ContractObjectiveRuntimeService _objectiveRuntime = new();

    private sealed class ContractObjectiveRuntimeService
    {
        public readonly HashSet<(EntityUid Store, string ContractId)> ActiveGhostRoleObjectives = new();
        public readonly HashSet<(EntityUid Store, string ContractId)> ActiveHuntObjectives = new();
        public readonly HashSet<(EntityUid Store, string ContractId)> ActiveRetrievalRouteDeliveries = new();
        public readonly HashSet<(EntityUid Store, string ContractId)> ActiveTrackedDeliveryDropoffObjectives = new();
        public readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByGuard = new();
        public readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByPinpointer = new();
        public readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByProof = new();
        public readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByRetrievalCargo = new();
        public readonly Dictionary<(EntityUid Store, string ContractId), ObjectiveRuntimeState> ByContract = new();
        public readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> ByTarget = new();
        public readonly List<(EntityUid Store, string ContractId)> KeysScratch = new();
        public readonly Dictionary<EntityUid, EntityUid> PinpointerOwners = new();

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
