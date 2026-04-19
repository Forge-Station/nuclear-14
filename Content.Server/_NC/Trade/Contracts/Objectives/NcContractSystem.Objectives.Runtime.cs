using System.Numerics;
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
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
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
    private int _activeTrackedDeliveryDropoffObjectives;
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
        _activeTrackedDeliveryDropoffObjectives = 0;
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

    private bool TryInitializeInventoryDeliverySupportRuntime(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var config = contract.Config;
        var spawnProtoId = config.DeliverySpawnPrototype;
        if (string.IsNullOrWhiteSpace(spawnProtoId))
            return true;

        if (!TryValidateInventoryDeliverySupportPrototype(contractId, spawnProtoId))
            return false;

        if (!TryResolveInventoryDeliverySupportCoordinates(store, contractId, config, out var spawnCoords))
            return false;

        var key = (store, contractId);
        return TryInitializeInventoryDeliverySupportGuards(key, config, spawnCoords)
            && TrySpawnInventoryDeliverySupportEntity(key, spawnProtoId, spawnCoords);
    }

    private bool TryValidateInventoryDeliverySupportPrototype(string contractId, string spawnProtoId)
    {
        if (_prototypes.HasIndex<EntityPrototype>(spawnProtoId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Delivery support init failed for '{contractId}': helper spawn prototype '{spawnProtoId}' is missing.");
        return false;
    }

    private bool TryResolveInventoryDeliverySupportCoordinates(
        EntityUid store,
        string contractId,
        ContractObjectiveConfigData config,
        out EntityCoordinates spawnCoords)
    {
        if (TryResolveObjectiveSpawnCoordinates(store, config, out spawnCoords))
            return true;

        Sawmill.Warning($"[Contracts] Delivery support init failed for '{contractId}': cannot resolve spawn coordinates.");
        return false;
    }

    private bool TryInitializeInventoryDeliverySupportGuards(
        (EntityUid Store, string ContractId) key,
        ContractObjectiveConfigData config,
        EntityCoordinates spawnCoords)
    {
        if (config.GuardCount <= 0 || string.IsNullOrWhiteSpace(config.GuardPrototype))
            return true;

        var state = GetOrCreateObjectiveRuntimeState(key);
        if (TrySpawnObjectiveGuards(key, state, config, spawnCoords))
            return true;

        CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities: false);
        return false;
    }

    private bool TrySpawnInventoryDeliverySupportEntity(
        (EntityUid Store, string ContractId) key,
        string spawnProtoId,
        EntityCoordinates spawnCoords)
    {
        try
        {
            Spawn(spawnProtoId, spawnCoords);
            return true;
        }
        catch (Exception e)
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities: false);
            Sawmill.Error(
                $"[Contracts] Delivery support init failed for '{key.ContractId}': cannot spawn helper item '{spawnProtoId}': {e}");
            return false;
        }
    }

    private bool TryInitializeDeliveryObjectiveRuntime(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var config = contract.Config;

        if (string.IsNullOrWhiteSpace(config.TargetPrototype))
            return true;

        if (!TryInitializeTrackedTargetAndSupport(
                store,
                user,
                contractId,
                contract,
                config.TargetPrototype,
                spawnGuards: true,
                spawnAtStore: config.SpawnAtStore))
            return false;

        return true;
    }

    private bool TryInitializeHuntObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var config = contract.Config;

        var targetProtoId = ResolveTrackedObjectivePrototypeId(config.TargetPrototype, contract.TargetItem);

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, targetProtoId))
            return false;

        config.TargetPrototype = targetProtoId;
        ResetObjectiveState(contract);

        return true;
    }

    private bool TryInitializeTrackedTargetAndSupport(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        string targetProtoId,
        bool spawnGuards = true,
        bool spawnAtStore = false
    )
    {
        if (!TryValidateObjectiveTargetPrototype(contractId, targetProtoId))
            return false;

        var config = contract.Config;
        if (!TryResolveTrackedTargetSpawnCoordinates(store, contractId, config, spawnAtStore, out var spawnCoords))
            return false;

        if (!TrySpawnObjectiveTarget(contractId, targetProtoId, spawnCoords, out var target))
            return false;

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        RegisterObjectiveTarget(key, state, target);

        if (!TryInitializeTrackedTargetDropoff(store, contractId, config, state))
            return CleanupFailedObjectiveInitialization(store, contractId);

        if (!TryInitializeTrackedTargetSupport(
                store,
                user,
                contract,
                key,
                state,
                target,
                spawnCoords,
                spawnGuards,
                config))
        {
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        return true;
    }

    private bool TryValidateObjectiveTargetPrototype(string contractId, string targetProtoId)
    {
        if (string.IsNullOrWhiteSpace(targetProtoId))
        {
            Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': target prototype is missing.");
            return false;
        }

        if (_prototypes.HasIndex<EntityPrototype>(targetProtoId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Objective init failed for '{contractId}': target prototype '{targetProtoId}' is missing.");
        return false;
    }

    private bool TryResolveTrackedTargetSpawnCoordinates(
        EntityUid store,
        string contractId,
        ContractObjectiveConfigData config,
        bool spawnAtStore,
        out EntityCoordinates spawnCoords
    )
    {
        if (spawnAtStore)
            return TryResolveStoreObjectiveCoordinates(store, contractId, out spawnCoords);

        if (TryResolveObjectiveSpawnCoordinates(store, config, out spawnCoords))
            return true;

        Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': cannot resolve spawn coordinates.");
        return false;
    }

    private bool TryResolveStoreObjectiveCoordinates(EntityUid store, string contractId, out EntityCoordinates spawnCoords)
    {
        spawnCoords = EntityCoordinates.Invalid;

        if (!TryComp(store, out TransformComponent? storeXform))
        {
            Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': store has no transform for local spawn.");
            return false;
        }

        spawnCoords = storeXform.Coordinates;
        return true;
    }

    private bool TrySpawnObjectiveTarget(string contractId, string targetProtoId, EntityCoordinates spawnCoords, out EntityUid target)
    {
        target = EntityUid.Invalid;

        try
        {
            target = Spawn(targetProtoId, spawnCoords);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error($"[Contracts] Objective init failed for '{contractId}': spawn '{targetProtoId}' threw: {e}");
            return false;
        }
    }

    private bool CleanupFailedObjectiveInitialization(EntityUid store, string contractId)
    {
        CleanupObjectiveRuntime(store, contractId, true);
        return false;
    }

    private void RegisterObjectiveTarget(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid target
    )
    {
        state.TargetEntity = target;
        _objectiveRuntimeByTarget[target] = key;
    }

    private bool TryInitializeTrackedTargetDropoff(
        EntityUid store,
        string contractId,
        ContractObjectiveConfigData config,
        ObjectiveRuntimeState state
    )
    {
        if (!HasConfiguredObjectiveDropoff(config))
        {
            DeactivateTrackedDeliveryDropoff(state);
            return true;
        }

        if (!TryResolveObjectiveDropoffCoordinates(store, config, out var dropoffCoords))
        {
            Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': cannot resolve dropoff coordinates.");
            return false;
        }

        state.DeliveryDropoffCoordinates = _xform.ToMapCoordinates(dropoffCoords);
        if (!TrySpawnDeliveryDropoffMarker(contractId, state, dropoffCoords))
            return false;

        ActivateTrackedDeliveryDropoff(state);
        return true;
    }

    private bool TryInitializeTrackedTargetSupport(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid target,
        EntityCoordinates spawnCoords,
        bool spawnGuards,
        ContractObjectiveConfigData config
    )
    {
        if (spawnGuards && !TrySpawnObjectiveGuards(key, state, config, spawnCoords))
            return false;

        if (!config.GivePinpointer)
            return true;

        var pinpointerTarget = ResolveObjectivePinpointerTarget(contract, state, target);
        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, config, spawnCoords);
    }

    // World spawning and pinpointer management.
    public bool TryIssueContractPinpointer(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
            return false;

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
            return false;

        if (!contract.Taken || contract.Completed)
            return false;

        EnsureObjectiveRuntimeDefaults(contract);

        var config = contract.Config;
        if (!config.GivePinpointer)
            return false;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return false;

        if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && !state.GhostRoleTaken)
            return false;

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid || TerminatingOrDeleted(target))
            return false;

        var pinpointerTarget = ResolveObjectivePinpointerTarget(contract, state, target);
        if (pinpointerTarget == EntityUid.Invalid || TerminatingOrDeleted(pinpointerTarget))
            return false;

        EntityCoordinates spawnCoords;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(target, out TransformComponent? targetXform))
            spawnCoords = targetXform.Coordinates;
        else
            return false;

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, config, spawnCoords);
    }

    private bool TrySpawnDeliveryDropoffMarker(
        string contractId,
        ObjectiveRuntimeState state,
        EntityCoordinates dropoffCoords)
    {
        EntityUid dropoffMarker;
        try
        {
            dropoffMarker = Spawn(NcContractTuning.DefaultTrackedDeliveryDropoffBeaconPrototypeId, dropoffCoords);
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{contractId}': cannot spawn dropoff beacon '{NcContractTuning.DefaultTrackedDeliveryDropoffBeaconPrototypeId}': {e}");
            return false;
        }

        state.DeliveryDropoffEntity = dropoffMarker;
        return true;
    }

    private void ActivateTrackedDeliveryDropoff(ObjectiveRuntimeState state)
    {
        if (state.ActiveDeliveryDropoff)
            return;

        state.DeliveryDropoffCompleted = false;
        state.ActiveDeliveryDropoff = true;
        _activeTrackedDeliveryDropoffObjectives++;
    }

    private void DeactivateTrackedDeliveryDropoff(ObjectiveRuntimeState state)
    {
        if (state.ActiveDeliveryDropoff)
        {
            state.ActiveDeliveryDropoff = false;

            if (_activeTrackedDeliveryDropoffObjectives > 0)
                _activeTrackedDeliveryDropoffObjectives--;
        }

        state.DeliveryDropoffCoordinates = null;

        if (state.DeliveryDropoffEntity is { } dropoffMarker)
        {
            state.DeliveryDropoffEntity = null;

            if (!TerminatingOrDeleted(dropoffMarker))
                Del(dropoffMarker);
        }
    }

    private static EntityUid ResolveObjectivePinpointerTarget(
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid fallbackTarget)
    {
        if (contract.IsTrackedDeliveryObjective &&
            UsesTrackedDeliveryDropoff(contract) &&
            state.DeliveryDropoffEntity is { } dropoffMarker &&
            dropoffMarker != EntityUid.Invalid)
        {
            return dropoffMarker;
        }

        return fallbackTarget;
    }

    private bool TrySpawnObjectivePinpointer(
        EntityUid user,
        EntityUid target,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config,
        EntityCoordinates spawnCoords
    )
    {
        if (!CanIssueContractPinpointer(key, state))
        {
            Sawmill.Info(
                $"[Contracts] Objective init blocked for '{key.ContractId}': contract pinpointer limit reached ({NcContractTuning.MaxActiveContractPinpointers}).");
            return false;
        }

        if (!TryResolveObjectivePinpointerPrototype(config, out var pinpointerProtoId))
            return false;

        var pinpointerCoords = ResolveObjectivePinpointerSpawnCoordinates(user, spawnCoords);
        if (!TrySpawnObjectivePinpointerEntity(key, pinpointerProtoId, pinpointerCoords, out var pinpointer))
            return false;

        RegisterObjectivePinpointer(user, target, key, state, pinpointer);
        return true;
    }

    private bool TryResolveObjectivePinpointerPrototype(
        ContractObjectiveConfigData config,
        out string pinpointerProtoId)
    {
        pinpointerProtoId = ResolvePinpointerPrototypeId(config.PinpointerPrototype);
        if (_prototypes.HasIndex<EntityPrototype>(pinpointerProtoId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Objective init: pinpointer proto '{pinpointerProtoId}' not found, fallback to {NcContractTuning.DefaultContractPinpointerPrototypeId}.");
        pinpointerProtoId = NcContractTuning.DefaultContractPinpointerPrototypeId;
        return _prototypes.HasIndex<EntityPrototype>(pinpointerProtoId);
    }

    private EntityCoordinates ResolveObjectivePinpointerSpawnCoordinates(EntityUid user, EntityCoordinates fallbackCoords)
    {
        if (TryComp(user, out TransformComponent? userXform))
            return userXform.Coordinates;

        return fallbackCoords;
    }

    private bool TrySpawnObjectivePinpointerEntity(
        (EntityUid Store, string ContractId) key,
        string pinpointerProtoId,
        EntityCoordinates pinpointerCoords,
        out EntityUid pinpointer)
    {
        try
        {
            pinpointer = Spawn(pinpointerProtoId, pinpointerCoords);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn pinpointer '{pinpointerProtoId}': {e}");
            pinpointer = EntityUid.Invalid;
            return false;
        }
    }

    private void RegisterObjectivePinpointer(
        EntityUid user,
        EntityUid target,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid pinpointer)
    {
        _pinpointer.SetTarget(pinpointer, target);
        _pinpointer.SetActive(pinpointer, true);
        state.PinpointerEntities.Add(pinpointer);
        _objectiveRuntimeByPinpointer[pinpointer] = key;
        _logic.QueuePickupToHandsOrCrateNextTick(user, pinpointer);
    }

    private bool TrySpawnObjectiveGuards(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config,
        EntityCoordinates spawnCoords
    )
    {
        if (!TryValidateObjectiveGuards(key, config, out var guardCount, out var guardPrototype))
            return guardCount >= 0;

        for (var i = 0; i < guardCount; i++)
        {
            var guardCoords = GetObjectiveGuardSpawnCoordinates(spawnCoords, i);
            if (!TrySpawnObjectiveGuard(key, state, guardPrototype, guardCoords))
                return false;
        }

        return true;
    }

    private bool TryValidateObjectiveGuards(
        (EntityUid Store, string ContractId) key,
        ContractObjectiveConfigData config,
        out int guardCount,
        out string guardPrototype)
    {
        guardCount = Math.Max(0, config.GuardCount);
        guardPrototype = config.GuardPrototype;
        if (guardCount <= 0 || string.IsNullOrWhiteSpace(guardPrototype))
            return false;

        if (_prototypes.HasIndex<EntityPrototype>(guardPrototype))
            return true;

        Sawmill.Warning(
            $"[Contracts] Objective init failed for '{key.ContractId}': guard prototype '{guardPrototype}' is missing.");
        guardCount = -1;
        return false;
    }

    private EntityCoordinates GetObjectiveGuardSpawnCoordinates(EntityCoordinates spawnCoords, int index)
    {
        var baseOffset = NcContractTuning.HuntGuardSpawnOffsets[index % NcContractTuning.HuntGuardSpawnOffsets.Length];
        var ring = index / NcContractTuning.HuntGuardSpawnOffsets.Length;
        var ringScale = 1f + ring * NcContractTuning.GuardSpawnRingScaleStep;
        var jitter = new Vector2(
            (_random.NextFloat() - 0.5f) * NcContractTuning.GuardSpawnJitterScale,
            (_random.NextFloat() - 0.5f) * NcContractTuning.GuardSpawnJitterScale);
        return spawnCoords.Offset(baseOffset * ringScale + jitter);
    }

    private bool TrySpawnObjectiveGuard(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        string guardPrototype,
        EntityCoordinates guardCoords)
    {
        try
        {
            var guard = Spawn(guardPrototype, guardCoords);
            state.GuardEntities.Add(guard);
            _objectiveRuntimeByGuard[guard] = key;
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn guard '{guardPrototype}': {e}");
            return false;
        }
    }

    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    )
    {
        return TryResolveObjectiveSpawnCoordinates(
            store,
            config.SpawnPointTag,
            config.SpawnPointTags,
            out coordinates,
            fallbackToStore);
    }

    private bool TryResolveObjectiveDropoffCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        out EntityCoordinates coordinates,
        bool fallbackToStore = false
    )
    {
        return TryResolveObjectiveSpawnCoordinates(
            store,
            config.DropoffPointTag,
            config.DropoffPointTags,
            out coordinates,
            fallbackToStore);
    }

    private static bool HasConfiguredObjectiveDropoff(ContractObjectiveConfigData config)
    {
        return !string.IsNullOrWhiteSpace(config.DropoffPointTag) ||
               config.DropoffPointTags is { Count: > 0 };
    }

    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        string? spawnTag,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    )
    {
        return TryResolveObjectiveSpawnCoordinates(store, spawnTag, null, out coordinates, fallbackToStore);
    }

    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        string? spawnTag,
        IReadOnlyList<WeightedTagEntry>? weightedSpawnTags,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    )
    {
        GetObjectiveSpawnFallback(store, out var storeXform, out coordinates);

        var selectedSpawnTag = ResolveObjectiveSpawnTag(storeXform?.MapID ?? MapId.Nullspace, spawnTag, weightedSpawnTags);

        if (string.IsNullOrWhiteSpace(selectedSpawnTag))
            return fallbackToStore && coordinates != EntityCoordinates.Invalid;

        if (!_prototypes.HasIndex<TagPrototype>(selectedSpawnTag))
            return HandleMissingObjectiveSpawnTag(selectedSpawnTag, coordinates, fallbackToStore);

        if (storeXform == null)
            return false;

        if (TryPickObjectiveSpawnCoordinate(storeXform.MapID, selectedSpawnTag, out var selectedCoordinates))
        {
            coordinates = selectedCoordinates;
            return true;
        }

        return HandleUnavailableObjectiveSpawnTag(store, selectedSpawnTag, coordinates, fallbackToStore);
    }

    private void GetObjectiveSpawnFallback(
        EntityUid store,
        out TransformComponent? storeXform,
        out EntityCoordinates coordinates
    )
    {
        if (TryComp(store, out storeXform))
        {
            coordinates = storeXform.Coordinates;
            return;
        }

        coordinates = EntityCoordinates.Invalid;
    }

    private string? ResolveObjectiveSpawnTag(
        MapId mapId,
        string? spawnTag,
        IReadOnlyList<WeightedTagEntry>? weightedSpawnTags
    )
    {
        var selectedSpawnTag = spawnTag;
        if (weightedSpawnTags is not { Count: > 0 })
            return selectedSpawnTag;

        var weightedTag = PickAvailableObjectiveSpawnTag(mapId, weightedSpawnTags);
        if (!string.IsNullOrWhiteSpace(weightedTag))
            selectedSpawnTag = weightedTag;

        return selectedSpawnTag;
    }

    private bool HandleMissingObjectiveSpawnTag(
        string selectedSpawnTag,
        EntityCoordinates fallbackCoordinates,
        bool fallbackToStore
    )
    {
        if (fallbackToStore)
        {
            Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' is not defined. Fallback to store coordinates.");
            return fallbackCoordinates != EntityCoordinates.Invalid;
        }

        Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' is not defined.");
        return false;
    }

    private bool TryPickObjectiveSpawnCoordinate(
        MapId storeMap,
        string selectedSpawnTag,
        out EntityCoordinates coordinates
    )
    {
        coordinates = EntityCoordinates.Invalid;
        var matches = 0;
        var found = false;

        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (query.MoveNext(out _, out var tagComp, out var xform))
        {
            if (xform.MapID != storeMap || !_tags.HasTag(tagComp, selectedSpawnTag))
                continue;

            matches++;
            if (_random.Next(matches) != 0)
                continue;

            coordinates = xform.Coordinates;
            found = true;
        }

        return found;
    }

    private bool HandleUnavailableObjectiveSpawnTag(
        EntityUid store,
        string selectedSpawnTag,
        EntityCoordinates fallbackCoordinates,
        bool fallbackToStore
    )
    {
        if (fallbackToStore)
        {
            Sawmill.Warning(
                $"[Contracts] Spawn tag '{selectedSpawnTag}' not found on map for {ToPrettyString(store)}. Fallback to store coordinates.");
            return fallbackCoordinates != EntityCoordinates.Invalid;
        }

        Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' not found on map for {ToPrettyString(store)}.");
        return false;
    }

    private string? PickAvailableObjectiveSpawnTag(
        MapId mapId,
        IReadOnlyList<WeightedTagEntry>? weightedSpawnTags
    )
    {
        if (weightedSpawnTags == null || weightedSpawnTags.Count == 0)
            return null;

        var totalWeight = 0;
        string? selectedTag = null;

        for (var i = 0; i < weightedSpawnTags.Count; i++)
        {
            var entry = weightedSpawnTags[i];
            if (string.IsNullOrWhiteSpace(entry.Tag) ||
                entry.Weight <= 0 ||
                !_prototypes.HasIndex<TagPrototype>(entry.Tag) ||
                !HasObjectiveSpawnTagOnMap(mapId, entry.Tag))
            {
                continue;
            }

            totalWeight += entry.Weight;
            if (_random.Next(totalWeight) < entry.Weight)
                selectedTag = entry.Tag;
        }

        return selectedTag;
    }

    private bool HasObjectiveSpawnTagOnMap(MapId mapId, string tag)
    {
        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (query.MoveNext(out _, out var tagComp, out var xform))
        {
            if (xform.MapID == mapId && _tags.HasTag(tagComp, tag))
                return true;
        }

        return false;
    }

    private bool CanIssueContractPinpointer((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        PruneInvalidPinpointers(key, state);
        return state.PinpointerEntities.Count < NcContractTuning.MaxActiveContractPinpointers;
    }

    private void PruneInvalidPinpointers((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _objectivePinpointersScratch.Clear();
        foreach (var pinpointer in state.PinpointerEntities)
            if (TerminatingOrDeleted(pinpointer))
                _objectivePinpointersScratch.Add(pinpointer);

        for (var i = 0; i < _objectivePinpointersScratch.Count; i++)
            UnregisterIssuedPinpointer(_objectivePinpointersScratch[i], key);

        _objectivePinpointersScratch.Clear();
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

    private void UnregisterIssuedPinpointer(EntityUid pinpointer, (EntityUid Store, string ContractId) key)
    {
        _objectiveRuntimeByPinpointer.Remove(pinpointer);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            state.PinpointerEntities.Remove(pinpointer);
    }

    private void CleanupObjectivePinpointers(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _objectivePinpointersScratch.Clear();
        _objectivePinpointersScratch.AddRange(state.PinpointerEntities);

        for (var i = 0; i < _objectivePinpointersScratch.Count; i++)
        {
            var pinpointer = _objectivePinpointersScratch[i];
            UnregisterIssuedPinpointer(pinpointer, key);

            if (!TerminatingOrDeleted(pinpointer))
                Del(pinpointer);
        }

        state.PinpointerEntities.Clear();
        _objectivePinpointersScratch.Clear();
    }

}
