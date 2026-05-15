using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Pinpointer;
using Content.Server.Tools;
using Content.Shared._NC.Trade;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    private readonly List<EntityUid> _objectivePinpointersScratch = new();

    private readonly Dictionary<(EntityUid Store, string ContractId), ObjectiveRuntimeState>
        _objectiveRuntimeByContract = new();

    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByGuard = new();
    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByPinpointer = new();
    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByProof = new();

    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByTarget = new();
    private readonly List<(EntityUid Store, string ContractId)> _objectiveRuntimeKeysScratch = new();

    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly ToolSystem _tool = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GhostRoleSystem _ghostRoles = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private void InitializeObjectiveRuntime()
    {
        SubscribeLocalEvent<EntityTerminatingEvent>(OnObjectiveTrackedEntityTerminating);
        SubscribeLocalEvent<MobStateChangedEvent>(OnObjectiveTrackedMobStateChanged);
        SubscribeLocalEvent<NcContractGhostRoleSpawnerComponent, GhostRoleGetRequirementsEvent>(OnContractGhostRoleGetRequirements);
        SubscribeLocalEvent<NcContractGhostRoleSpawnerComponent, TakeGhostRoleEvent>(OnContractGhostRoleTakeover);
        SubscribeLocalEvent<NcContractRepairObjectiveComponent, InteractUsingEvent>(OnRepairObjectiveInteractUsing);
        SubscribeLocalEvent<NcContractRepairObjectiveComponent, ContractRepairDoAfterEvent>(OnRepairObjectiveDoAfter);
    }

    private TimeSpan _nextGhostRoleTimeoutCheck = TimeSpan.Zero;
    private TimeSpan _nextTrackedDeliveryDropoffCheck = TimeSpan.Zero;
    private TimeSpan _nextRetrievalRouteDeliveryCheck = TimeSpan.Zero;
    private int _activeTrackedDeliveryDropoffObjectives;
    private int _activeRetrievalRouteDeliveries;
    private void ShutdownObjectiveRuntime() => ClearAllObjectiveRuntime(false, deleteGuards: false);
    public override void Update(float frameTime)
    {
        if (_objectiveRuntimeByContract.Count == 0)
            return;

        if (_activeTrackedDeliveryDropoffObjectives > 0 && _timing.CurTime >= _nextTrackedDeliveryDropoffCheck)
        {
            _nextTrackedDeliveryDropoffCheck = _timing.CurTime + NcContractTuning.TrackedDeliveryDropoffCheckInterval;
            UpdateTrackedDeliveryDropoffObjectives();
        }

        if (_activeRetrievalRouteDeliveries > 0 && _timing.CurTime >= _nextRetrievalRouteDeliveryCheck)
        {
            _nextRetrievalRouteDeliveryCheck = _timing.CurTime + NcContractTuning.TrackedDeliveryDropoffCheckInterval;
            UpdateRetrievalRouteDeliveries();
        }

        if (_timing.CurTime < _nextGhostRoleTimeoutCheck)
            return;

        _nextGhostRoleTimeoutCheck = _timing.CurTime + NcContractTuning.GhostRoleTimeoutCheckInterval;
        UpdateGhostRoleObjectiveTimeouts();
    }

    private void ClearAllObjectiveRuntime(bool deleteTrackedEntities, bool deleteGuards = true)
    {
        if (_objectiveRuntimeByContract.Count == 0)
            return;

        _objectiveRuntimeKeysScratch.Clear();
        foreach (var key in _objectiveRuntimeByContract.Keys)
            _objectiveRuntimeKeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
        {
            var key = _objectiveRuntimeKeysScratch[i];
            CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities, deleteGuards);
        }

        _objectiveRuntimeKeysScratch.Clear();
        _objectiveRuntimeByTarget.Clear();
        _objectiveRuntimeByPinpointer.Clear();
        _objectiveRuntimeByGuard.Clear();
        _objectiveRuntimeByProof.Clear();   // Fix (B39): keep proof index in sync with everything else.
        _activeTrackedDeliveryDropoffObjectives = 0;
        _activeRetrievalRouteDeliveries = 0;
    }

    private void ClearStoreObjectiveRuntime(EntityUid store, bool deleteTrackedEntities, bool deleteGuards = true)
    {
        if (store == EntityUid.Invalid || _objectiveRuntimeByContract.Count == 0)
            return;

        _objectiveRuntimeKeysScratch.Clear();
        foreach (var key in _objectiveRuntimeByContract.Keys)
            if (key.Store == store)
                _objectiveRuntimeKeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
        {
            var key = _objectiveRuntimeKeysScratch[i];
            CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities, deleteGuards);
        }

        _objectiveRuntimeKeysScratch.Clear();
    }

    // Objective initialization.
    private bool TryInitializeObjectiveRuntimeOnTake(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        CleanupObjectiveRuntime(store, contractId, true);

        EnsureObjectiveRuntimeDefaults(contract);

        if (!TryValidateObjectiveProofPrototype(contractId, contract))
            return false;

        return contract.ExecutionKind switch
        {
            ContractExecutionKind.InventoryDelivery => TryInitializeInventoryDeliverySupportRuntime(store, user, contractId, contract),
            ContractExecutionKind.TrackedDeliveryObjective => TryInitializeDeliveryObjectiveRuntime(store, user, contractId, contract),
            ContractExecutionKind.HuntObjective => TryInitializeHuntObjective(store, user, contractId, contract),
            ContractExecutionKind.RepairObjective => TryInitializeRepairObjective(store, user, contractId, contract),
            ContractExecutionKind.GhostRoleObjective => TryInitializeGhostRoleObjective(store, user, contractId, contract),
            _ => true
        };
    }

    private ObjectiveRuntimeState GetOrCreateObjectiveRuntimeState((EntityUid Store, string ContractId) key)
    {
        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return state;

        state = new();
        _objectiveRuntimeByContract[key] = state;
        return state;
    }

    private bool TryGetObjectiveContract(
        (EntityUid Store, string ContractId) key,
        out NcStoreComponent comp,
        out ContractServerData contract
    )
    {
        comp = default!;
        contract = default!;

        if (!TryComp(key.Store, out NcStoreComponent? storeComp) || storeComp == null)
            return false;

        if (!storeComp.Contracts.TryGetValue(key.ContractId, out var foundContract) || foundContract == null)
            return false;

        comp = storeComp;
        contract = foundContract;
        return true;
    }
}
