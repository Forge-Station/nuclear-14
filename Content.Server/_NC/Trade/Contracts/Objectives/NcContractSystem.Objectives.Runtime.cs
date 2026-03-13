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

        if (!_prototypes.HasIndex<EntityPrototype>(spawnProtoId))
        {
            Sawmill.Warning(
                $"[Contracts] Delivery support init failed for '{contractId}': helper spawn prototype '{spawnProtoId}' is missing.");
            return false;
        }

        if (!TryResolveObjectiveSpawnCoordinates(store, config, out var spawnCoords))
        {
            Sawmill.Warning($"[Contracts] Delivery support init failed for '{contractId}': cannot resolve spawn coordinates.");
            return false;
        }

        var key = (store, contractId);
        if (config.GuardCount > 0 && !string.IsNullOrWhiteSpace(config.GuardPrototype))
        {
            var state = GetOrCreateObjectiveRuntimeState(key);
            if (!TrySpawnObjectiveGuards(key, state, config, spawnCoords))
            {
                CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: false);
                return false;
            }
        }

        try
        {
            Spawn(spawnProtoId, spawnCoords);
        }
        catch (Exception e)
        {
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: false);
            Sawmill.Error(
                $"[Contracts] Delivery support init failed for '{contractId}': cannot spawn helper item '{spawnProtoId}': {e}");
            return false;
        }

        return true;
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
        if (string.IsNullOrWhiteSpace(targetProtoId) || !_prototypes.HasIndex<EntityPrototype>(targetProtoId))
        {
            Sawmill.Warning(
                $"[Contracts] Objective init failed for '{contractId}': target prototype '{targetProtoId}' is missing.");
            return false;
        }

        EntityCoordinates spawnCoords;
        if (spawnAtStore)
        {
            if (!TryComp(store, out TransformComponent? storeXform))
            {
                Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': store has no transform for local spawn.");
                return false;
            }

            spawnCoords = storeXform.Coordinates;
        }
        else if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out spawnCoords))
        {
            Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': cannot resolve spawn coordinates.");
            return false;
        }

        EntityUid target;
        try
        {
            target = Spawn(targetProtoId, spawnCoords);
        }
        catch (Exception e)
        {
            Sawmill.Error($"[Contracts] Objective init failed for '{contractId}': spawn '{targetProtoId}' threw: {e}");
            return false;
        }

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = target;
        _objectiveRuntimeByTarget[target] = key;

        if (HasConfiguredObjectiveDropoff(contract.Config))
        {
            if (!TryResolveObjectiveDropoffCoordinates(store, contract.Config, out var dropoffCoords))
            {
                CleanupObjectiveRuntime(store, contractId, true);
                Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': cannot resolve dropoff coordinates.");
                return false;
            }

            state.DeliveryDropoffCoordinates = _xform.ToMapCoordinates(dropoffCoords);

            if (!TrySpawnDeliveryDropoffMarker(contractId, state, dropoffCoords))
            {
                CleanupObjectiveRuntime(store, contractId, true);
                return false;
            }

            ActivateTrackedDeliveryDropoff(state);
        }
        else
        {
            DeactivateTrackedDeliveryDropoff(state);
        }

        if (spawnGuards && !TrySpawnObjectiveGuards(key, state, contract.Config, spawnCoords))
        {
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        if (!contract.Config.GivePinpointer)
            return true;

        var pinpointerTarget = ResolveObjectivePinpointerTarget(contract, state, target);
        if (!TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, contract.Config, spawnCoords))
        {
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        return true;
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

        if (!contract.Config.GivePinpointer)
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

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, contract.Config, spawnCoords);
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

        var pinpointerProtoId = ResolvePinpointerPrototypeId(config.PinpointerPrototype);

        if (!_prototypes.HasIndex<EntityPrototype>(pinpointerProtoId))
        {
            Sawmill.Warning(
                $"[Contracts] Objective init: pinpointer proto '{pinpointerProtoId}' not found, fallback to {NcContractTuning.DefaultContractPinpointerPrototypeId}.");
            pinpointerProtoId = NcContractTuning.DefaultContractPinpointerPrototypeId;

            if (!_prototypes.HasIndex<EntityPrototype>(pinpointerProtoId))
                return false;
        }

        EntityCoordinates pinpointerCoords;
        if (TryComp(user, out TransformComponent? userXform))
            pinpointerCoords = userXform.Coordinates;
        else
            pinpointerCoords = spawnCoords;

        EntityUid pinpointer;
        try
        {
            pinpointer = Spawn(pinpointerProtoId, pinpointerCoords);
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn pinpointer '{pinpointerProtoId}': {e}");
            return false;
        }

        _pinpointer.SetTarget(pinpointer, target);
        _pinpointer.SetActive(pinpointer, true);

        state.PinpointerEntities.Add(pinpointer);
        _objectiveRuntimeByPinpointer[pinpointer] = key;
        _logic.QueuePickupToHandsOrCrateNextTick(user, pinpointer);

        return true;
    }

    private bool TrySpawnObjectiveGuards(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config,
        EntityCoordinates spawnCoords
    )
    {
        var guardCount = Math.Max(0, config.GuardCount);
        if (guardCount <= 0 || string.IsNullOrWhiteSpace(config.GuardPrototype))
            return true;

        var guardPrototype = config.GuardPrototype;
        if (!_prototypes.HasIndex<EntityPrototype>(guardPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Objective init failed for '{key.ContractId}': guard prototype '{guardPrototype}' is missing.");
            return false;
        }

        for (var i = 0; i < guardCount; i++)
        {
            var baseOffset = NcContractTuning.HuntGuardSpawnOffsets[i % NcContractTuning.HuntGuardSpawnOffsets.Length];
            var ring = i / NcContractTuning.HuntGuardSpawnOffsets.Length;
            var ringScale = 1f + ring * NcContractTuning.GuardSpawnRingScaleStep;
            var jitter = new Vector2(
                (_random.NextFloat() - 0.5f) * NcContractTuning.GuardSpawnJitterScale,
                (_random.NextFloat() - 0.5f) * NcContractTuning.GuardSpawnJitterScale);
            var guardCoords = spawnCoords.Offset(baseOffset * ringScale + jitter);

            EntityUid guard;
            try
            {
                guard = Spawn(guardPrototype, guardCoords);
            }
            catch (Exception e)
            {
                Sawmill.Error(
                    $"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn guard '{guardPrototype}': {e}");
                return false;
            }

            state.GuardEntities.Add(guard);
            _objectiveRuntimeByGuard[guard] = key;
        }

        return true;
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
               config.DropoffPointTags.Count > 0;
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
        if (TryComp(store, out TransformComponent? storeXform))
            coordinates = storeXform.Coordinates;
        else
            coordinates = EntityCoordinates.Invalid;

        var selectedSpawnTag = spawnTag;
        if (weightedSpawnTags is { Count: > 0 })
        {
            var weightedTag = PickAvailableObjectiveSpawnTag(storeXform?.MapID ?? MapId.Nullspace, weightedSpawnTags);
            if (!string.IsNullOrWhiteSpace(weightedTag))
                selectedSpawnTag = weightedTag;
        }

        if (string.IsNullOrWhiteSpace(selectedSpawnTag))
            return fallbackToStore && coordinates != EntityCoordinates.Invalid;

        if (!_prototypes.HasIndex<TagPrototype>(selectedSpawnTag))
        {
            if (fallbackToStore)
            {
                Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' is not defined. Fallback to store coordinates.");
                return coordinates != EntityCoordinates.Invalid;
            }

            Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' is not defined.");
            return false;
        }

        if (storeXform == null)
            return false;

        var storeMap = storeXform.MapID;
        var found = false;
        var matches = 0;

        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (query.MoveNext(out _, out var tagComp, out var xform))
        {
            if (xform.MapID != storeMap)
                continue;

            if (!_tags.HasTag(tagComp, selectedSpawnTag))
                continue;

            matches++;
            if (_random.Next(matches) != 0)
                continue;

            coordinates = xform.Coordinates;
            found = true;
        }

        if (found)
            return true;

        if (fallbackToStore)
        {
            Sawmill.Warning(
                $"[Contracts] Spawn tag '{selectedSpawnTag}' not found on map for {ToPrettyString(store)}. Fallback to store coordinates.");
            return coordinates != EntityCoordinates.Invalid;
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
