using Content.Shared._NC.Trade;
using Content.Shared.Mind;
using Robust.Shared.Map;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static bool IsTargetInEntityContainer(TransformComponent xform)
    {
        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid)
            return false;

        if (xform.MapUid is { } mapUid && parent == mapUid)
            return false;

        if (xform.GridUid is { } gridUid && parent == gridUid)
            return false;

        return true;
    }

    private void UpdateObjectiveContractProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        if (TryGetObjectiveHandler(contract.ExecutionKind, out var handler))
        {
            handler.RefreshObjectiveProgress(this, store, contractId, contract);
            return;
        }

        SyncObjectiveProgressFromRuntime(contract);
        ResetContractTargetProgress(contract);
        SyncContractFlowStatus(contract);
    }

    internal sealed class ObjectiveRuntimeState
    {
        public bool ActiveDeliveryDropoff;
        public bool DeliveryDropoffCompleted;
        public MapCoordinates? DeliveryDropoffCoordinates;
        public EntityUid? DeliveryDropoffEntity;
        public readonly List<EntityUid> GuardEntities = new();
        public readonly HashSet<EntityUid> PinpointerEntities = new();
        public readonly Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int> TurnedInByTarget = new();
        public readonly List<EntityUid> RetrievalSpawnedEntities = new();
        public readonly HashSet<EntityUid> RetrievalSpawnedEntitySet = new();
        public readonly List<EntityUid> HuntSpawnedTargets = new();
        public readonly HashSet<EntityUid> RetrievalDeliveredEntities = new();
        public int RetrievalAcceptedCargoCount;
        public EntityCoordinates? RetrievalLastAcceptedCargoCoordinates;
        public bool RetrievalRouteDeliveryActive;
        public bool RetrievalRouteDeliveryCompleted;
        public EntityCoordinates? RetrievalDeliveryCoordinates;
        public TimeSpan? GhostRoleAcceptDeadline;
        public TimeSpan? GhostRoleSurvivalStart;
        public TimeSpan? GhostRoleSurvivalDeadline;
        public EntityUid? GhostRoleSurvivalMind;
        public EntityUid? GhostRoleSurvivalObjective;
        public long GhostRoleRoundEndId;
        public bool GhostRoleSurvivalSucceeded;
        public bool GhostRoleTaken;
        public bool HuntTargetWasKilled;
        public bool HuntActive;
        public EntityUid? HuntBodyEntity;
        public EntityCoordinates? LastKnownTargetCoordinates;
        public EntityUid? ProofEntity;
        public bool ProofSpawned;
        public string ProofToken = string.Empty;
        public EntityUid? TargetEntity;
    }
}
