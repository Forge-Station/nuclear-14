using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Pinpointer;
using Content.Shared._NC.Trade;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Objectives.Components;
using Content.Shared.Tag;
using Robust.Shared.Timing;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly List<EntityUid> _objectivePinpointersScratch = new();

    private readonly Dictionary<(EntityUid Store, string ContractId), ObjectiveRuntimeState>
        _objectiveRuntimeByContract = new();

    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByGuard = new();
    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByPinpointer = new();
    private readonly Dictionary<EntityUid, EntityUid> _objectiveRuntimePinpointerOwners = new();
    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByProof = new();

    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByTarget = new();
    private readonly List<(EntityUid Store, string ContractId)> _objectiveRuntimeKeysScratch = new();

    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GhostRoleSystem _ghostRoles = default!;
    [Dependency] private readonly MindSystem _contractMind = default!;
    [Dependency] private readonly MetaDataSystem _contractMeta = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private void InitializeObjectiveRuntime()
    {
        SubscribeLocalEvent<EntityTerminatingEvent>(OnObjectiveTrackedEntityTerminating);
        SubscribeLocalEvent<MobStateChangedEvent>(OnObjectiveTrackedMobStateChanged);
        SubscribeLocalEvent<NcContractGhostRoleSpawnerComponent, GhostRoleGetRequirementsEvent>(OnContractGhostRoleGetRequirements);
        SubscribeLocalEvent<NcContractGhostRoleSpawnerComponent, TakeGhostRoleEvent>(OnContractGhostRoleTakeover);
        SubscribeLocalEvent<NcContractGhostRoleSurvivalObjectiveComponent, ObjectiveGetProgressEvent>(
            OnGhostRoleSurvivalObjectiveGetProgress);
        SubscribeLocalEvent<EntParentChangedMessage>(OnObjectiveTrackedEntityParentChanged);
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnObjectiveTrackedDamageChanged);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnGhostRoleRoundEndText);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnGhostRoleRoundRestartCleanup);
    }

    private void OnGhostRoleSurvivalObjectiveGetProgress(
        EntityUid uid,
        NcContractGhostRoleSurvivalObjectiveComponent component,
        ref ObjectiveGetProgressEvent args)
    {
        if (component.Finished)
        {
            args.Progress = component.Succeeded ? 1f : 0f;
            _contractMeta.SetEntityName(
                uid,
                Loc.GetString("nc-store-contract-ghost-role-survival-objective-title-done"));
            return;
        }

        var total = (component.Deadline - component.StartedAt).TotalSeconds;
        if (total <= 0)
        {
            args.Progress = 1f;
            _contractMeta.SetEntityName(
                uid,
                Loc.GetString("nc-store-contract-ghost-role-survival-objective-title-live", ("time", FormatGhostRoleCountdown(0))));
            return;
        }

        var elapsed = (_timing.CurTime - component.StartedAt).TotalSeconds;
        var remaining = Math.Max(0, (int) Math.Ceiling((component.Deadline - _timing.CurTime).TotalSeconds));
        _contractMeta.SetEntityName(
            uid,
            Loc.GetString(
                "nc-store-contract-ghost-role-survival-objective-title-live",
                ("time", FormatGhostRoleCountdown(remaining))));
        args.Progress = Math.Clamp((float) (elapsed / total), 0f, 1f);
    }

    private static string FormatGhostRoleCountdown(int totalSeconds)
    {
        var clamped = Math.Max(0, totalSeconds);
        var span = TimeSpan.FromSeconds(clamped);
        return span.TotalHours >= 1
            ? span.ToString(@"hh\:mm\:ss")
            : span.ToString(@"mm\:ss");
    }

    private void OnObjectiveTrackedEntityParentChanged(ref EntParentChangedMessage args)
    {
        if (_objectiveRuntimeByProof.TryGetValue(args.Entity, out var key))
        {
            if (_objectiveRuntimeByContract.TryGetValue(key, out var state) &&
                TryGetObjectiveContract(key, out _, out var contract) &&
                contract.Taken &&
                !contract.Runtime.Failed &&
                TryResolveRetrievalRouteReturnPinpointerTarget(key.Store, contract, state, out var target))
            {
                if (target == key.Store && TryGetContainedEntityRoot(args.Entity, out var proofCarrier))
                    RetargetObjectivePinpointersForOwner(key, state, proofCarrier, target);
                else
                    RetargetObjectivePinpointers(key, state, target);
            }

            return;
        }

        if (TryResolveRetrievalSpawnedParentChangePinpointerTarget(
                args.Entity,
                out var spawnedKey,
                out var spawnedState,
                out var spawnedTarget,
                out var spawnedCarrier))
        {
            if (spawnedCarrier != EntityUid.Invalid)
                RetargetObjectivePinpointersForOwner(spawnedKey, spawnedState, spawnedCarrier, spawnedTarget);
            else
                RetargetObjectivePinpointers(spawnedKey, spawnedState, spawnedTarget);
        }
    }

    private TimeSpan _nextGhostRoleTimeoutCheck = TimeSpan.Zero;
    private TimeSpan _nextTrackedDeliveryDropoffCheck = TimeSpan.Zero;
    private TimeSpan _nextRetrievalRouteDeliveryCheck = TimeSpan.Zero;
    private TimeSpan _nextHuntPinpointerCheck = TimeSpan.Zero;
    private int _activeTrackedDeliveryDropoffObjectives;
    private int _activeRetrievalRouteDeliveries;
    private int _activeHuntObjectives;
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

        if (_activeHuntObjectives > 0 && _timing.CurTime >= _nextHuntPinpointerCheck)
        {
            _nextHuntPinpointerCheck = _timing.CurTime + NcContractTuning.TrackedDeliveryDropoffCheckInterval;
            UpdateSpawnedHuntPinpointerTargets();
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
        _objectiveRuntimePinpointerOwners.Clear();
        _objectiveRuntimeByGuard.Clear();
        _objectiveRuntimeByProof.Clear();   // Fix (B39): keep proof index in sync with everything else.
        _activeTrackedDeliveryDropoffObjectives = 0;
        _activeRetrievalRouteDeliveries = 0;
        _activeHuntObjectives = 0;
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
            ContractExecutionKind.HuntObjective => TryInitializeHuntObjectiveRuntimeOnTake(store, user, contractId, contract),
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
