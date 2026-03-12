using System;
using System.Numerics;
using Content.Server.Pinpointer;
using Content.Shared._NC.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private const int MaxActiveContractPinpointers = 5;

    private static readonly Vector2[] HuntGuardSpawnOffsets =
    {
        new(0.9f, 0f),
        new(-0.9f, 0f),
        new(0f, 0.9f),
        new(0f, -0.9f),
        new(0.75f, 0.75f),
        new(-0.75f, 0.75f),
        new(0.75f, -0.75f),
        new(-0.75f, -0.75f)
    };

    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly Dictionary<(EntityUid Store, string ContractId), ObjectiveRuntimeState> _objectiveRuntimeByContract = new();
    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByTarget = new();
    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByPinpointer = new();
    private readonly Dictionary<EntityUid, (EntityUid Store, string ContractId)> _objectiveRuntimeByGuard = new();
    private readonly List<(EntityUid Store, string ContractId)> _objectiveRuntimeKeysScratch = new();
    private readonly List<EntityUid> _objectivePinpointersScratch = new();

    private sealed class ObjectiveRuntimeState
    {
        public EntityUid? TargetEntity;
        public readonly HashSet<EntityUid> PinpointerEntities = new();
        public readonly List<EntityUid> GuardEntities = new();
    }

    private void InitializeObjectiveRuntime()
    {
        SubscribeLocalEvent<EntityTerminatingEvent>(OnObjectiveTrackedEntityTerminating);
        SubscribeLocalEvent<MobStateChangedEvent>(OnObjectiveTrackedMobStateChanged);
    }

    private void ShutdownObjectiveRuntime()
    {
        ClearAllObjectiveRuntime(deleteTrackedEntities: false);
    }

    private void ClearAllObjectiveRuntime(bool deleteTrackedEntities)
    {
        if (_objectiveRuntimeByContract.Count == 0)
            return;

        _objectiveRuntimeKeysScratch.Clear();
        foreach (var key in _objectiveRuntimeByContract.Keys)
            _objectiveRuntimeKeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
        {
            var key = _objectiveRuntimeKeysScratch[i];
            CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities);
        }

        _objectiveRuntimeKeysScratch.Clear();
        _objectiveRuntimeByTarget.Clear();
        _objectiveRuntimeByPinpointer.Clear();
        _objectiveRuntimeByGuard.Clear();
    }

    private void ClearStoreObjectiveRuntime(EntityUid store, bool deleteTrackedEntities)
    {
        if (store == EntityUid.Invalid || _objectiveRuntimeByContract.Count == 0)
            return;

        _objectiveRuntimeKeysScratch.Clear();
        foreach (var key in _objectiveRuntimeByContract.Keys)
        {
            if (key.Store == store)
                _objectiveRuntimeKeysScratch.Add(key);
        }

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
        {
            var key = _objectiveRuntimeKeysScratch[i];
            CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities);
        }

        _objectiveRuntimeKeysScratch.Clear();
    }

    private bool TryInitializeObjectiveRuntimeOnTake(EntityUid store, EntityUid user, string contractId, ContractServerData contract)
    {
        CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);

        EnsureObjectiveRuntimeDefaults(contract);

        return contract.ObjectiveType switch
        {
            ContractObjectiveType.Delivery => TryInitializeDeliveryObjectiveRuntime(store, user, contractId, contract),
            ContractObjectiveType.Hunt => TryInitializeHuntObjective(store, user, contractId, contract),
            ContractObjectiveType.Repair => true,
            ContractObjectiveType.GhostRole => true,
            _ => true
        };
    }

    private bool TryInitializeDeliveryObjectiveRuntime(EntityUid store, EntityUid user, string contractId, ContractServerData contract)
    {
        var runtime = contract.Runtime;

        // For regular delivery contracts we keep old behavior (no runtime world entities).
        if (string.IsNullOrWhiteSpace(runtime.TargetPrototype))
            return true;

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, runtime.TargetPrototype))
            return false;

        return true;
    }

    private bool TryInitializeHuntObjective(EntityUid store, EntityUid user, string contractId, ContractServerData contract)
    {
        var runtime = contract.Runtime;

        var targetProtoId = !string.IsNullOrWhiteSpace(runtime.TargetPrototype)
            ? runtime.TargetPrototype
            : contract.TargetItem;

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, targetProtoId))
            return false;

        runtime.TargetPrototype = targetProtoId;
        runtime.Stage = 0;
        runtime.Failed = false;
        runtime.FailureReason = string.Empty;

        contract.Required = Math.Max(1, runtime.StageGoal);
        contract.Progress = 0;

        return true;
    }

    private bool TryInitializeTrackedTargetAndSupport(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        string targetProtoId)
    {
        if (string.IsNullOrWhiteSpace(targetProtoId) || !_prototypes.HasIndex<EntityPrototype>(targetProtoId))
        {
            Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': target prototype '{targetProtoId}' is missing.");
            return false;
        }

        if (!TryResolveObjectiveSpawnCoordinates(store, contract.Runtime.SpawnPointTag, out var spawnCoords))
        {
            Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': cannot resolve spawn coordinates.");
            return false;
        }

        EntityUid target;
        try
        {
            target = EntityManager.SpawnEntity(targetProtoId, spawnCoords);
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

        if (!TrySpawnObjectiveGuards(key, state, contract.Runtime, spawnCoords))
        {
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
            return false;
        }

        if (!contract.Runtime.GivePinpointer)
            return true;

        if (!TrySpawnObjectivePinpointer(user, target, key, state, contract.Runtime, spawnCoords))
        {
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
            return false;
        }

        return true;
    }

    public bool TryIssueContractPinpointer(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
            return false;

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
            return false;

        if (!contract.Taken || contract.Completed)
            return false;

        EnsureObjectiveRuntimeDefaults(contract);

        if (!contract.Runtime.GivePinpointer)
            return false;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return false;

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid || TerminatingOrDeleted(target))
            return false;

        EntityCoordinates spawnCoords;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(target, out TransformComponent? targetXform))
            spawnCoords = targetXform.Coordinates;
        else
            return false;

        return TrySpawnObjectivePinpointer(user, target, key, state, contract.Runtime, spawnCoords);
    }
    private bool TrySpawnObjectivePinpointer(
        EntityUid user,
        EntityUid target,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractRuntimeContextData runtime,
        EntityCoordinates spawnCoords)
    {
        if (!CanIssueContractPinpointer(key, state))
        {
            Sawmill.Info($"[Contracts] Objective init blocked for '{key.ContractId}': contract pinpointer limit reached ({MaxActiveContractPinpointers}).");
            return false;
        }

        var pinpointerProtoId = !string.IsNullOrWhiteSpace(runtime.PinpointerPrototype)
            ? runtime.PinpointerPrototype
            : "PinpointerUniversal";

        if (!_prototypes.HasIndex<EntityPrototype>(pinpointerProtoId))
        {
            Sawmill.Warning($"[Contracts] Objective init: pinpointer proto '{pinpointerProtoId}' not found, fallback to PinpointerUniversal.");
            pinpointerProtoId = "PinpointerUniversal";

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
            pinpointer = EntityManager.SpawnEntity(pinpointerProtoId, pinpointerCoords);
        }
        catch (Exception e)
        {
            Sawmill.Error($"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn pinpointer '{pinpointerProtoId}': {e}");
            return false;
        }

        _pinpointer.SetTarget(pinpointer, target);
        _pinpointer.SetActive(pinpointer, true);

        state.PinpointerEntities.Add(pinpointer);
        _objectiveRuntimeByPinpointer[pinpointer] = key;

        return true;
    }

    private bool TrySpawnObjectiveGuards(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractRuntimeContextData runtime,
        EntityCoordinates spawnCoords)
    {
        var guardCount = Math.Max(0, runtime.GuardCount);
        if (guardCount <= 0 || string.IsNullOrWhiteSpace(runtime.GuardPrototype))
            return true;

        var guardPrototype = runtime.GuardPrototype;
        if (!_prototypes.HasIndex<EntityPrototype>(guardPrototype))
        {
            Sawmill.Warning($"[Contracts] Objective init failed for '{key.ContractId}': guard prototype '{guardPrototype}' is missing.");
            return false;
        }

        for (var i = 0; i < guardCount; i++)
        {
            var baseOffset = HuntGuardSpawnOffsets[i % HuntGuardSpawnOffsets.Length];
            var ring = i / HuntGuardSpawnOffsets.Length;
            var ringScale = 1f + ring * 0.65f;
            var jitter = new Vector2((_random.NextFloat() - 0.5f) * 0.2f, (_random.NextFloat() - 0.5f) * 0.2f);
            var guardCoords = spawnCoords.Offset(baseOffset * ringScale + jitter);

            EntityUid guard;
            try
            {
                guard = EntityManager.SpawnEntity(guardPrototype, guardCoords);
            }
            catch (Exception e)
            {
                Sawmill.Error($"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn guard '{guardPrototype}': {e}");
                return false;
            }

            state.GuardEntities.Add(guard);
            _objectiveRuntimeByGuard[guard] = key;
        }

        return true;
    }

    private bool TryResolveObjectiveSpawnCoordinates(EntityUid store, string? spawnTag, out EntityCoordinates coordinates)
    {
        if (TryComp(store, out TransformComponent? storeXform))
            coordinates = storeXform.Coordinates;
        else
            coordinates = EntityCoordinates.Invalid;

        if (string.IsNullOrWhiteSpace(spawnTag))
            return coordinates != EntityCoordinates.Invalid;

        if (!_prototypes.HasIndex<TagPrototype>(spawnTag))
        {
            Sawmill.Warning($"[Contracts] Spawn tag '{spawnTag}' is not defined. Fallback to store coordinates.");
            return coordinates != EntityCoordinates.Invalid;
        }

        if (storeXform == null)
            return false;

        var storeMap = storeXform.MapID;
        var storeWorld = _xform.GetWorldPosition(storeXform);
        var bestDistance = float.MaxValue;
        var found = false;

        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (query.MoveNext(out var _, out var tagComp, out var xform))
        {
            if (xform.MapID != storeMap)
                continue;

            if (!_tags.HasTag(tagComp, spawnTag))
                continue;

            var candidateWorld = _xform.GetWorldPosition(xform);
            var dist = (candidateWorld - storeWorld).LengthSquared();
            if (dist >= bestDistance)
                continue;

            bestDistance = dist;
            coordinates = xform.Coordinates;
            found = true;
        }

        if (found)
            return true;

        Sawmill.Warning($"[Contracts] Spawn tag '{spawnTag}' not found on map for {ToPrettyString(store)}. Fallback to store coordinates.");
        return coordinates != EntityCoordinates.Invalid;
    }

    private bool CanIssueContractPinpointer((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        PruneInvalidPinpointers(key, state);
        return state.PinpointerEntities.Count < MaxActiveContractPinpointers;
    }

    private void PruneInvalidPinpointers((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _objectivePinpointersScratch.Clear();
        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                _objectivePinpointersScratch.Add(pinpointer);
        }

        for (var i = 0; i < _objectivePinpointersScratch.Count; i++)
            UnregisterIssuedPinpointer(_objectivePinpointersScratch[i], key);

        _objectivePinpointersScratch.Clear();
    }

    private ObjectiveRuntimeState GetOrCreateObjectiveRuntimeState((EntityUid Store, string ContractId) key)
    {
        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return state;

        state = new ObjectiveRuntimeState();
        _objectiveRuntimeByContract[key] = state;
        return state;
    }

    private void UnregisterIssuedPinpointer(EntityUid pinpointer, (EntityUid Store, string ContractId) key)
    {
        _objectiveRuntimeByPinpointer.Remove(pinpointer);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state))
            state.PinpointerEntities.Remove(pinpointer);
    }

    private void CleanupObjectivePinpointers((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state, bool deleteTrackedEntities)
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _objectivePinpointersScratch.Clear();
        _objectivePinpointersScratch.AddRange(state.PinpointerEntities);

        for (var i = 0; i < _objectivePinpointersScratch.Count; i++)
        {
            var pinpointer = _objectivePinpointersScratch[i];
            UnregisterIssuedPinpointer(pinpointer, key);

            if (deleteTrackedEntities && !TerminatingOrDeleted(pinpointer))
                EntityManager.DeleteEntity(pinpointer);
        }

        state.PinpointerEntities.Clear();
        _objectivePinpointersScratch.Clear();
    }

    private void OnObjectiveTrackedEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (_objectiveRuntimeByTarget.TryGetValue(args.Entity, out var targetKey))
            OnObjectiveTrackedTargetResolved(targetKey, args.Entity);

        if (_objectiveRuntimeByPinpointer.TryGetValue(args.Entity, out var pinpointerKey))
            UnregisterIssuedPinpointer(args.Entity, pinpointerKey);

        if (_objectiveRuntimeByGuard.Remove(args.Entity, out var guardKey) &&
            _objectiveRuntimeByContract.TryGetValue(guardKey, out var guardState))
        {
            guardState.GuardEntities.Remove(args.Entity);
        }
    }

    private void OnObjectiveTrackedMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        if (!_objectiveRuntimeByTarget.TryGetValue(args.Target, out var key))
            return;

        OnObjectiveTrackedTargetResolved(key, args.Target);
    }

    private void OnObjectiveTrackedTargetResolved((EntityUid Store, string ContractId) key, EntityUid target)
    {
        _objectiveRuntimeByTarget.Remove(target);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state) && state.TargetEntity == target)
            state.TargetEntity = null;

        if (!TryComp(key.Store, out NcStoreComponent? comp))
            return;

        if (!comp.Contracts.TryGetValue(key.ContractId, out var contract))
            return;

        if (!contract.Taken || contract.ObjectiveType != ContractObjectiveType.Hunt)
            return;

        EnsureObjectiveRuntimeDefaults(contract);

        var runtime = contract.Runtime;
        if (runtime.Failed)
            return;

        var stageGoal = Math.Max(1, runtime.StageGoal);
        runtime.Stage = stageGoal;

        contract.Required = stageGoal;
        contract.Progress = stageGoal;

        if (_objectiveRuntimeByContract.TryGetValue(key, out var huntState))
            CleanupObjectivePinpointers(key, huntState, deleteTrackedEntities: true);
    }

    private static void EnsureObjectiveRuntimeDefaults(ContractServerData contract)
    {
        contract.Runtime ??= new ContractRuntimeContextData();
        var runtime = contract.Runtime;

        if (runtime.StageGoal <= 0)
            runtime.StageGoal = contract.ObjectiveType == ContractObjectiveType.Repair ? 3 : 1;

        runtime.AcceptTimeoutSeconds = Math.Max(0, runtime.AcceptTimeoutSeconds);
        runtime.GuardCount = Math.Max(0, runtime.GuardCount);
        runtime.Stage = Math.Clamp(runtime.Stage, 0, runtime.StageGoal);

        contract.Required = Math.Max(1, runtime.StageGoal);
        contract.Progress = Math.Clamp(runtime.Stage, 0, contract.Required);

        if (contract.ObjectiveType == ContractObjectiveType.Delivery)
            return;

        if (!string.IsNullOrWhiteSpace(contract.TargetItem))
            return;

        if (!string.IsNullOrWhiteSpace(runtime.TargetPrototype))
            contract.TargetItem = runtime.TargetPrototype;
        else if (!string.IsNullOrWhiteSpace(runtime.StructurePrototype))
            contract.TargetItem = runtime.StructurePrototype;
        else if (!string.IsNullOrWhiteSpace(runtime.GhostRolePrototype))
            contract.TargetItem = runtime.GhostRolePrototype;
    }

    private void CleanupObjectiveRuntime(EntityUid store, string contractId, bool deleteTrackedEntities, bool deleteGuards = true)
    {
        var key = (store, contractId);

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is { } target)
        {
            _objectiveRuntimeByTarget.Remove(target);
            state.TargetEntity = null;

            if (deleteTrackedEntities && !TerminatingOrDeleted(target))
                EntityManager.DeleteEntity(target);
        }

        CleanupObjectivePinpointers(key, state, deleteTrackedEntities);

        if (state.GuardEntities.Count > 0)
        {
            for (var i = 0; i < state.GuardEntities.Count; i++)
            {
                var guard = state.GuardEntities[i];
                _objectiveRuntimeByGuard.Remove(guard);

                if (deleteTrackedEntities && deleteGuards && !TerminatingOrDeleted(guard))
                    EntityManager.DeleteEntity(guard);
            }

            state.GuardEntities.Clear();
        }

        _objectiveRuntimeByContract.Remove(key);
    }

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

    private void SyncHuntObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid)
            return;

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return;
        }

        if (TryComp(target, out MobStateComponent? mobState))
        {
            if (mobState.CurrentState == MobState.Dead)
                OnObjectiveTrackedTargetResolved(key, target);

            return;
        }

        if (TryComp(target, out TransformComponent? targetXform) && IsTargetInEntityContainer(targetXform))
            OnObjectiveTrackedTargetResolved(key, target);
    }

    private void UpdateObjectiveContractProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        if (contract.ObjectiveType == ContractObjectiveType.Hunt)
            SyncHuntObjectiveProgress(store, contractId, contract);

        var runtime = contract.Runtime;
        var stageGoal = Math.Max(1, runtime.StageGoal);
        var stage = Math.Clamp(runtime.Stage, 0, stageGoal);

        contract.Required = stageGoal;
        contract.Progress = stage;

        if (contract.Targets.Count > 0)
        {
            for (var i = 0; i < contract.Targets.Count; i++)
            {
                var t = contract.Targets[i];
                t.Progress = 0;
                contract.Targets[i] = t;
            }
        }
    }
}
