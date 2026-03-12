using System.Numerics;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Pinpointer;
using Content.Server.Mind.Commands;
using Content.Server.Tools;
using Content.Shared._NC.Trade;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private const int MaxActiveContractPinpointers = 5;
    private const float GhostRoleStoreDeliveryRange = 2.5f;

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

    private static readonly TimeSpan GhostRoleTimeoutCheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextGhostRoleTimeoutCheck = TimeSpan.Zero;
    private void ShutdownObjectiveRuntime() => ClearAllObjectiveRuntime(false);
    public override void Update(float frameTime)
    {
        if (_objectiveRuntimeByContract.Count == 0 || _timing.CurTime < _nextGhostRoleTimeoutCheck)
            return;
        _nextGhostRoleTimeoutCheck = _timing.CurTime + GhostRoleTimeoutCheckInterval;
        UpdateGhostRoleObjectiveTimeouts();
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
            if (key.Store == store)
                _objectiveRuntimeKeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
        {
            var key = _objectiveRuntimeKeysScratch[i];
            CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities);
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

        return contract.ObjectiveType switch
        {
            ContractObjectiveType.Delivery => TryInitializeDeliveryObjectiveRuntime(store, user, contractId, contract),
            ContractObjectiveType.Hunt => TryInitializeHuntObjective(store, user, contractId, contract),
            ContractObjectiveType.Repair => TryInitializeRepairObjective(store, user, contractId, contract),
            ContractObjectiveType.GhostRole => TryInitializeGhostRoleObjective(store, user, contractId, contract),
            _ => true
        };
    }

    private bool TryInitializeDeliveryObjectiveRuntime(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var runtime = contract.Runtime;

        // For regular delivery contracts we keep old behavior (no runtime world entities).
        if (string.IsNullOrWhiteSpace(runtime.TargetPrototype))
            return true;

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, runtime.TargetPrototype))
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
        var runtime = contract.Runtime;

        var targetProtoId = ResolveTrackedObjectivePrototypeId(runtime.TargetPrototype, contract.TargetItem);

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, targetProtoId))
            return false;

        runtime.TargetPrototype = targetProtoId;
        ResetObjectiveState(contract);

        return true;
    }


    private bool TryInitializeGhostRoleObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var runtime = contract.Runtime;
        var ghostRoleProtoId = ResolveTrackedObjectivePrototypeId(runtime.GhostRolePrototype, contract.TargetItem);
        if (string.IsNullOrWhiteSpace(ghostRoleProtoId) || !_prototypes.HasIndex<EntityPrototype>(ghostRoleProtoId))
        {
            Sawmill.Warning($"[Contracts] Ghost role init failed for '{contractId}': ghost role prototype '{ghostRoleProtoId}' is missing.");
            return false;
        }

        runtime.GhostRolePrototype = ghostRoleProtoId;
        ResetObjectiveState(contract);

        if (!TryResolveObjectiveSpawnCoordinates(store, runtime.SpawnPointTag, out var spawnCoords))
        {
            Sawmill.Warning($"[Contracts] Ghost role init failed for '{contractId}': cannot resolve spawn coordinates.");
            return false;
        }

        EntityUid spawner;
        try
        {
            spawner = Spawn(null, spawnCoords);
        }
        catch (Exception e)
        {
            Sawmill.Error($"[Contracts] Ghost role init failed for '{contractId}': runtime spawner creation threw: {e}");
            return false;
        }

        var ghostRole = EnsureComp<GhostRoleComponent>(spawner);
        ghostRole.RoleName = contract.Name;
        ghostRole.RoleDescription = contract.Description;

        var spawnerComp = EnsureComp<NcContractGhostRoleSpawnerComponent>(spawner);
        spawnerComp.TargetPrototype = ghostRoleProtoId;

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = spawner;
        state.GhostRoleTaken = false;
        state.GhostRoleAcceptDeadline = runtime.AcceptTimeoutSeconds > 0
            ? _timing.CurTime + TimeSpan.FromSeconds(runtime.AcceptTimeoutSeconds)
            : null;
        _objectiveRuntimeByTarget[spawner] = key;

        runtime.GhostRolePendingAcceptance = state.GhostRoleAcceptDeadline != null;
        runtime.AcceptTimeoutRemainingSeconds = runtime.GhostRolePendingAcceptance
            ? Math.Max(0, runtime.AcceptTimeoutSeconds)
            : 0;

        return true;
    }
    private void OnContractGhostRoleTakeover(EntityUid uid, NcContractGhostRoleSpawnerComponent comp, ref TakeGhostRoleEvent args)
    {
        if (!TryComp(uid, out GhostRoleComponent? ghostRole) || comp.Claimed || ghostRole.Taken || MetaData(uid).EntityPaused)
        {
            args.TookRole = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(comp.TargetPrototype) || !_prototypes.HasIndex<EntityPrototype>(comp.TargetPrototype))
        {
            Sawmill.Warning($"[Contracts] Ghost role take failed for {ToPrettyString(uid)}: invalid prototype '{comp.TargetPrototype}'.");
            args.TookRole = false;
            return;
        }

        var mob = Spawn(comp.TargetPrototype, Transform(uid).Coordinates);
        _xform.AttachToGridOrMap(mob);

        if (!TryActivateGhostRoleContractTarget(uid, mob))
        {
            QueueDel(mob);
            args.TookRole = false;
            return;
        }

        if (ghostRole.MakeSentient)
            MakeSentientCommand.MakeSentient(mob, EntityManager, ghostRole.AllowMovement, ghostRole.AllowSpeech);

        EnsureComp<MindContainerComponent>(mob);
        _ghostRoles.GhostRoleInternalCreateMindAndTransfer(args.Player, uid, mob, ghostRole);

        comp.Claimed = true;
        _ghostRoles.UnregisterGhostRole((uid, ghostRole));
        QueueDel(uid);

        args.TookRole = true;
    }

    private bool TryActivateGhostRoleContractTarget(EntityUid spawner, EntityUid target)
    {
        if (!_objectiveRuntimeByTarget.TryGetValue(spawner, out var key))
            return false;

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return false;

        if (!TryGetObjectiveContract(key, out _, out var contract) ||
            !contract.Taken ||
            contract.ObjectiveType != ContractObjectiveType.GhostRole)
        {
            return false;
        }

        _objectiveRuntimeByTarget.Remove(spawner);
        state.TargetEntity = target;
        state.GhostRoleTaken = true;
        state.GhostRoleAcceptDeadline = null;
        contract.Runtime.GhostRolePendingAcceptance = false;
        contract.Runtime.AcceptTimeoutRemainingSeconds = 0;
        _objectiveRuntimeByTarget[target] = key;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (!TerminatingOrDeleted(pinpointer))
                _pinpointer.SetTarget(pinpointer, target);
        }

        if (contract.Runtime.GuardCount <= 0 || string.IsNullOrWhiteSpace(contract.Runtime.GuardPrototype))
            return true;

        if (!TryComp(target, out TransformComponent? targetXform))
            return true;

        if (TrySpawnObjectiveGuards(key, state, contract.Runtime, targetXform.Coordinates))
            return true;

        Sawmill.Warning($"[Contracts] Ghost role guard wave failed for '{key.ContractId}'.");
        return true;
    }
    private bool TryInitializeTrackedTargetAndSupport(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        string targetProtoId,
        bool spawnGuards = true
    )
    {
        if (string.IsNullOrWhiteSpace(targetProtoId) || !_prototypes.HasIndex<EntityPrototype>(targetProtoId))
        {
            Sawmill.Warning(
                $"[Contracts] Objective init failed for '{contractId}': target prototype '{targetProtoId}' is missing.");
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

        if (spawnGuards && !TrySpawnObjectiveGuards(key, state, contract.Runtime, spawnCoords))
        {
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        if (!contract.Runtime.GivePinpointer)
            return true;

        if (!TrySpawnObjectivePinpointer(user, target, key, state, contract.Runtime, spawnCoords))
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

        if (!contract.Runtime.GivePinpointer)
            return false;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return false;

        if (contract.ObjectiveType == ContractObjectiveType.GhostRole && !state.GhostRoleTaken)
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
        EntityCoordinates spawnCoords
    )
    {
        if (!CanIssueContractPinpointer(key, state))
        {
            Sawmill.Info(
                $"[Contracts] Objective init blocked for '{key.ContractId}': contract pinpointer limit reached ({MaxActiveContractPinpointers}).");
            return false;
        }

        var pinpointerProtoId = ResolvePinpointerPrototypeId(runtime.PinpointerPrototype);

        if (!_prototypes.HasIndex<EntityPrototype>(pinpointerProtoId))
        {
            Sawmill.Warning(
                $"[Contracts] Objective init: pinpointer proto '{pinpointerProtoId}' not found, fallback to {DefaultContractPinpointerPrototypeId}.");
            pinpointerProtoId = DefaultContractPinpointerPrototypeId;

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

        return true;
    }

    private bool TrySpawnObjectiveGuards(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractRuntimeContextData runtime,
        EntityCoordinates spawnCoords
    )
    {
        var guardCount = Math.Max(0, runtime.GuardCount);
        if (guardCount <= 0 || string.IsNullOrWhiteSpace(runtime.GuardPrototype))
            return true;

        var guardPrototype = runtime.GuardPrototype;
        if (!_prototypes.HasIndex<EntityPrototype>(guardPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Objective init failed for '{key.ContractId}': guard prototype '{guardPrototype}' is missing.");
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
        string? spawnTag,
        out EntityCoordinates coordinates
    )
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
        while (query.MoveNext(out _, out var tagComp, out var xform))
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

        Sawmill.Warning(
            $"[Contracts] Spawn tag '{spawnTag}' not found on map for {ToPrettyString(store)}. Fallback to store coordinates.");
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
        ObjectiveRuntimeState state,
        bool deleteTrackedEntities
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

            if (deleteTrackedEntities && !TerminatingOrDeleted(pinpointer))
                Del(pinpointer);
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
            guardState.GuardEntities.Remove(args.Entity);
    }


    // Repair objective runtime.
    private bool TryInitializeRepairObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var runtime = contract.Runtime;

        var structureProtoId = ResolveTrackedObjectivePrototypeId(runtime.StructurePrototype, contract.TargetItem);

        if (string.IsNullOrWhiteSpace(structureProtoId))
        {
            Sawmill.Warning($"[Contracts] Repair init failed for '{contractId}': structure prototype is missing.");
            return false;
        }

        runtime.StructurePrototype = structureProtoId;
        ResetObjectiveState(contract);

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, structureProtoId, false))
            return false;

        var key = (store, contractId);
        if (_objectiveRuntimeByContract.TryGetValue(key, out var state) && state.TargetEntity is { } structure)
        {
            var repair = EnsureComp<NcContractRepairObjectiveComponent>(structure);
            repair.ToolQuality = runtime.RepairToolQuality;
            repair.DoAfterSeconds = runtime.RepairDoAfterSeconds;
        }

        return true;
    }

    private void OnRepairObjectiveInteractUsing(
        EntityUid uid,
        NcContractRepairObjectiveComponent comp,
        InteractUsingEvent args
    )
    {
        if (args.Handled)
            return;

        if (!TryGetRepairRuntimeState(uid, out _, out var runtimeState))
            return;

        if (runtimeState.RepairInProgress)
        {
            args.Handled = true;
            return;
        }

        if (!TryGetActiveRepairContract(uid, out _, out _, out var contract))
            return;

        var quality = ResolveRepairToolQuality(
            string.IsNullOrWhiteSpace(comp.ToolQuality) ? contract.Runtime.RepairToolQuality : comp.ToolQuality);

        var delay = ResolveRepairDoAfterSeconds(
            comp.DoAfterSeconds > 0f ? comp.DoAfterSeconds : contract.Runtime.RepairDoAfterSeconds);

        runtimeState.RepairInProgress = true;

        var started = _tool.UseTool(args.Used, args.User, uid, delay, quality, new ContractRepairDoAfterEvent());
        if (!started)
            runtimeState.RepairInProgress = false;

        args.Handled = started;
    }

    private void OnRepairObjectiveDoAfter(
        EntityUid uid,
        NcContractRepairObjectiveComponent comp,
        ContractRepairDoAfterEvent args
    )
    {
        if (!TryGetRepairRuntimeState(uid, out _, out var runtimeState))
            return;

        runtimeState.RepairInProgress = false;

        if (args.Cancelled)
            return;

        if (!TryGetActiveRepairContract(uid, out var key, out var state, out var contract))
            return;

        var runtime = contract.Runtime;
        var stageGoal = Math.Max(1, runtime.StageGoal);
        if (runtime.Stage >= stageGoal)
            return;

        runtime.Stage = Math.Clamp(runtime.Stage + 1, 0, stageGoal);
        SyncObjectiveProgressFromRuntime(contract);

        PlayRepairObjectiveStageEffects(uid, runtime);

        if (runtime.GuardCount <= 0 || string.IsNullOrWhiteSpace(runtime.GuardPrototype))
            return;

        if (TryComp(uid, out TransformComponent? structureXform) &&
            !TrySpawnObjectiveGuards(key, state, runtime, structureXform.Coordinates))
            Sawmill.Warning($"[Contracts] Repair stage wave failed for '{key.ContractId}'.");
    }

    private bool TryGetRepairRuntimeState(
        EntityUid uid,
        out (EntityUid Store, string ContractId) key,
        out ObjectiveRuntimeState state
    )
    {
        key = default;
        state = default!;

        if (!_objectiveRuntimeByTarget.TryGetValue(uid, out key))
            return false;

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var foundState) ||
            foundState == null ||
            foundState.TargetEntity != uid)
        {
            return false;
        }

        state = foundState;
        return true;
    }

    private bool TryGetActiveRepairContract(
        EntityUid uid,
        out (EntityUid Store, string ContractId) key,
        out ObjectiveRuntimeState state,
        out ContractServerData contract
    )
    {
        key = default;
        state = default!;
        contract = default!;

        if (!TryGetRepairRuntimeState(uid, out key, out state))
            return false;

        if (!TryGetObjectiveContract(key, out _, out contract))
            return false;

        if (!contract.Taken || contract.ObjectiveType != ContractObjectiveType.Repair || contract.Completed)
            return false;

        EnsureObjectiveRuntimeDefaults(contract);
        return !contract.Runtime.Failed;
    }

    private void PlayRepairObjectiveStageEffects(EntityUid structure, ContractRuntimeContextData runtime)
    {
        var sound = ResolveRepairStageSound(runtime.RepairStageSound);

        _audio.PlayPvs(
            sound,
            structure,
            AudioParams.Default.WithVariation(0.125f).WithVolume(-1f));

        var hadJitter = HasComp<JitteringComponent>(structure);
        _jitter.AddJitter(structure, 12f, 7f);
        if (hadJitter)
            return;

        Timer.Spawn(
            TimeSpan.FromSeconds(1.2),
            () =>
            {
                if (TerminatingOrDeleted(structure))
                    return;

                RemComp<JitteringComponent>(structure);
            });
    }

    // Ghost role objective runtime.
    private void UpdateGhostRoleObjectiveTimeouts()
    {
        if (_objectiveRuntimeByContract.Count == 0)
            return;
        _objectiveRuntimeKeysScratch.Clear();
        foreach (var (key, state) in _objectiveRuntimeByContract)
        {
            if (state.GhostRoleTaken || state.GhostRoleAcceptDeadline is not { } deadline)
                continue;
            if (_timing.CurTime >= deadline)
                _objectiveRuntimeKeysScratch.Add(key);
        }
        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
            FailExpiredGhostRoleObjective(_objectiveRuntimeKeysScratch[i]);
        _objectiveRuntimeKeysScratch.Clear();
    }
    private void FailExpiredGhostRoleObjective((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            state.GhostRoleTaken ||
            state.GhostRoleAcceptDeadline is not { } deadline ||
            _timing.CurTime < deadline)
        {
            return;
        }
        if (!TryGetObjectiveContract(key, out var comp, out var contract))
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, true);
            return;
        }
        if (!contract.Taken || contract.ObjectiveType != ContractObjectiveType.GhostRole || contract.Completed)
            return;
        contract.Runtime.Failed = true;
        contract.Runtime.GhostRolePendingAcceptance = false;
        contract.Runtime.AcceptTimeoutRemainingSeconds = 0;
        contract.Runtime.FailureReason = Loc.GetString("nc-store-contract-ghost-role-timeout");
        CleanupObjectivePinpointers(key, state, true);
        FailObjectiveContract(key, comp, deleteGuards: false);
    }
    // Target resolution and progress synchronization.
    private void OnObjectiveTrackedMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        if (!_objectiveRuntimeByTarget.TryGetValue(args.Target, out var key))
            return;

        if (!TryGetObjectiveContract(key, out _, out var contract) || contract.ObjectiveType != ContractObjectiveType.Hunt)
            return;

        OnObjectiveTrackedTargetResolved(key, args.Target);
    }

    private void OnObjectiveTrackedTargetResolved((EntityUid Store, string ContractId) key, EntityUid target)
    {
        _objectiveRuntimeByTarget.Remove(target);

        if (_objectiveRuntimeByContract.TryGetValue(key, out var state) && state.TargetEntity == target)
            state.TargetEntity = null;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken)
            return;

        EnsureObjectiveRuntimeDefaults(contract);
        if (contract.Runtime.Failed)
            return;

        switch (contract.ObjectiveType)
        {
            case ContractObjectiveType.Repair:
                HandleRepairObjectiveTargetResolved(key, comp, contract);
                return;

            case ContractObjectiveType.Hunt:
                HandleHuntObjectiveTargetResolved(key, contract);
                return;

            case ContractObjectiveType.GhostRole:
                HandleGhostRoleTargetResolved(key, comp, contract);
                return;

            default:
                return;
        }
    }

    private void HandleRepairObjectiveTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (contract.Completed)
        {
            if (_objectiveRuntimeByContract.TryGetValue(key, out var completedRepairState))
                CleanupObjectivePinpointers(key, completedRepairState, true);
            return;
        }

        contract.Runtime.Failed = true;
        contract.Runtime.FailureReason = Loc.GetString("nc-store-contract-repair-structure-lost");

        if (_objectiveRuntimeByContract.TryGetValue(key, out var failedRepairState))
            CleanupObjectivePinpointers(key, failedRepairState, true);

        FailObjectiveContract(key, comp, deleteGuards: false);
    }

    private void HandleHuntObjectiveTargetResolved((EntityUid Store, string ContractId) key, ContractServerData contract)
    {
        MarkObjectiveComplete(contract);
        if (_objectiveRuntimeByContract.TryGetValue(key, out var huntState))
            CleanupObjectivePinpointers(key, huntState, true);
    }
    private void HandleGhostRoleTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        contract.Runtime.Failed = true;
        contract.Runtime.FailureReason = Loc.GetString("nc-store-contract-ghost-role-target-lost");

        if (_objectiveRuntimeByContract.TryGetValue(key, out var failedGhostRoleState))
            CleanupObjectivePinpointers(key, failedGhostRoleState, true);

        FailObjectiveContract(key, comp, deleteGuards: false);
    }
    // Shared objective state helpers.
    private static void EnsureObjectiveRuntimeDefaults(ContractServerData contract)
    {
        var runtime = contract.Runtime;
        NormalizeRuntimeContext(contract.ObjectiveType, runtime);

        if (contract.ObjectiveType == ContractObjectiveType.Delivery)
            return;

        SyncObjectiveProgressFromRuntime(contract);

        if (!string.IsNullOrWhiteSpace(contract.TargetItem))
            return;

        contract.TargetItem = ResolveObjectiveTargetId(runtime);
    }

    private static void ResetObjectiveState(ContractServerData contract)
    {
        var runtime = contract.Runtime;
        runtime.Stage = 0;
        runtime.Failed = false;
        runtime.FailureReason = string.Empty;
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;

        contract.Required = Math.Max(1, runtime.StageGoal);
        contract.Progress = 0;
    }

    private static void SyncObjectiveProgressFromRuntime(ContractServerData contract)
    {
        var stageGoal = Math.Max(1, contract.Runtime.StageGoal);
        contract.Required = stageGoal;
        contract.Progress = Math.Clamp(contract.Runtime.Stage, 0, stageGoal);
    }

    private static void MarkObjectiveComplete(ContractServerData contract)
    {
        contract.Runtime.Stage = Math.Max(1, contract.Runtime.StageGoal);
        SyncObjectiveProgressFromRuntime(contract);
    }

    private void FailObjectiveContract((EntityUid Store, string ContractId) key, NcStoreComponent comp, bool deleteGuards)
    {
        CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities: true, deleteGuards: deleteGuards);
        comp.Contracts.Remove(key.ContractId);
        RefillContractsForStore(key.Store, comp, key.ContractId);
    }

    private void CleanupObjectiveRuntime(
        EntityUid store,
        string contractId,
        bool deleteTrackedEntities,
        bool deleteGuards = true
    )
    {
        var key = (store, contractId);

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is { } target)
        {
            _objectiveRuntimeByTarget.Remove(target);
            RemComp<NcContractRepairObjectiveComponent>(target);
            state.TargetEntity = null;

            if (deleteTrackedEntities && !TerminatingOrDeleted(target))
                Del(target);
        }

        CleanupObjectivePinpointers(key, state, deleteTrackedEntities);

        if (state.GuardEntities.Count > 0)
        {
            for (var i = 0; i < state.GuardEntities.Count; i++)
            {
                var guard = state.GuardEntities[i];
                _objectiveRuntimeByGuard.Remove(guard);

                if (deleteTrackedEntities && deleteGuards && !TerminatingOrDeleted(guard))
                    Del(guard);
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


    public bool HasRealtimeGhostRoleState(NcStoreComponent comp)
    {
        foreach (var contract in comp.Contracts.Values)
        {
            if (!contract.Taken || contract.ObjectiveType != ContractObjectiveType.GhostRole)
                continue;

            EnsureObjectiveRuntimeDefaults(contract);
            if (!contract.Runtime.Failed)
                return true;
        }

        return false;
    }
    private bool IsGhostRoleTargetAtStore(EntityUid store, EntityUid target)
    {
        if (!TryComp(store, out TransformComponent? storeXform) || !TryComp(target, out TransformComponent? targetXform))
            return false;

        if (storeXform.MapID != targetXform.MapID)
            return false;

        var storePos = _xform.GetWorldPosition(storeXform);
        var targetPos = _xform.GetWorldPosition(targetXform);
        return (targetPos - storePos).LengthSquared() <= GhostRoleStoreDeliveryRange * GhostRoleStoreDeliveryRange;
    }

    private void SyncGhostRoleObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        var runtime = contract.Runtime;

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
        {
            runtime.GhostRolePendingAcceptance = false;
            runtime.AcceptTimeoutRemainingSeconds = 0;
            return;
        }

        if (!state.GhostRoleTaken && state.GhostRoleAcceptDeadline is { } deadline)
        {
            runtime.GhostRolePendingAcceptance = true;
            runtime.AcceptTimeoutRemainingSeconds = Math.Max(0, (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds));
            runtime.Stage = 0;
            return;
        }

        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid)
            return;

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return;
        }

        var isDead = TryComp(target, out MobStateComponent? mobState) && mobState.CurrentState == MobState.Dead;
        contract.Runtime.Stage = state.GhostRoleTaken && isDead && IsGhostRoleTargetAtStore(store, target)
            ? Math.Max(1, contract.Runtime.StageGoal)
            : 0;
    }
    private void UpdateObjectiveContractProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        if (contract.ObjectiveType == ContractObjectiveType.Hunt)
            SyncHuntObjectiveProgress(store, contractId, contract);
        else if (contract.ObjectiveType == ContractObjectiveType.GhostRole)
            SyncGhostRoleObjectiveProgress(store, contractId, contract);

        SyncObjectiveProgressFromRuntime(contract);

        ResetContractTargetProgress(contract);
    }

    private sealed class ObjectiveRuntimeState
    {
        public readonly List<EntityUid> GuardEntities = new();
        public readonly HashSet<EntityUid> PinpointerEntities = new();
        public TimeSpan? GhostRoleAcceptDeadline;
        public bool GhostRoleTaken;
        public bool RepairInProgress;
        public EntityUid? TargetEntity;
    }
}









