using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind.Commands;
using Content.Shared._NC.Trade;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryInitializeGhostRoleObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        if (!TryResolveGhostRolePrototype(contractId, contract, out var ghostRoleProtoId))
            return false;

        var config = EnsureContractConfig(contract);
        ResetObjectiveState(contract);
        config.GhostRolePrototype = ghostRoleProtoId;
        if (!TryResolveGhostRoleSpawnCoordinates(store, contractId, config, out var spawnCoords))
            return false;

        if (!TrySpawnGhostRoleSpawner(contractId, spawnCoords, out var spawner))
            return false;

        ConfigureGhostRoleSpawner(spawner, contract, ghostRoleProtoId);
        RegisterGhostRoleObjectiveState((store, contractId), spawner, contract);
        return true;
    }

    private bool TryResolveGhostRolePrototype(
        string contractId,
        ContractServerData contract,
        out string ghostRoleProtoId)
    {
        ghostRoleProtoId = ResolveTrackedObjectivePrototypeId(EnsureContractConfig(contract).GhostRolePrototype, contract.TargetItem);
        if (!string.IsNullOrWhiteSpace(ghostRoleProtoId) && _prototypes.HasIndex<EntityPrototype>(ghostRoleProtoId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Ghost role init failed for '{contractId}': ghost role prototype '{ghostRoleProtoId}' is missing.");
        return false;
    }

    private bool TryResolveGhostRoleSpawnCoordinates(
        EntityUid store,
        string contractId,
        ContractObjectiveConfigData config,
        out EntityCoordinates spawnCoords)
    {
        if (TryResolveObjectiveSpawnCoordinates(store, config, out spawnCoords))
            return true;

        Sawmill.Warning($"[Contracts] Ghost role init failed for '{contractId}': cannot resolve spawn coordinates.");
        return false;
    }

    private bool TrySpawnGhostRoleSpawner(
        string contractId,
        EntityCoordinates spawnCoords,
        out EntityUid spawner)
    {
        try
        {
            spawner = Spawn(null, spawnCoords);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error($"[Contracts] Ghost role init failed for '{contractId}': runtime spawner creation threw: {e}");
            spawner = EntityUid.Invalid;
            return false;
        }
    }

    private void ConfigureGhostRoleSpawner(EntityUid spawner, ContractServerData contract, string ghostRoleProtoId)
    {
        var ghostRole = EnsureComp<GhostRoleComponent>(spawner);
        ghostRole.RoleName = contract.Name;
        ghostRole.RoleDescription = contract.Description;

        var spawnerComp = EnsureComp<NcContractGhostRoleSpawnerComponent>(spawner);
        spawnerComp.TargetPrototype = ghostRoleProtoId;
    }

    private void RegisterGhostRoleObjectiveState(
        (EntityUid Store, string ContractId) key,
        EntityUid spawner,
        ContractServerData contract)
    {
        var config = EnsureContractConfig(contract);
        var runtime = EnsureContractRuntime(contract);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = spawner;
        state.GhostRoleTaken = false;
        state.GhostRoleAcceptDeadline = config.AcceptTimeoutSeconds > 0
            ? _timing.CurTime + TimeSpan.FromSeconds(config.AcceptTimeoutSeconds)
            : null;
        _objectiveRuntimeByTarget[spawner] = key;

        runtime.GhostRolePendingAcceptance = state.GhostRoleAcceptDeadline != null;
        runtime.AcceptTimeoutRemainingSeconds = runtime.GhostRolePendingAcceptance
            ? Math.Max(0, config.AcceptTimeoutSeconds)
            : 0;
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
            contract.Completed ||
            !contract.IsGhostRoleObjective ||
            EnsureContractRuntime(contract).Failed)
        {
            return false;
        }

        EnsureObjectiveRuntimeDefaults(contract);

        if (!state.GhostRoleTaken && state.GhostRoleAcceptDeadline is { } deadline && _timing.CurTime >= deadline)
        {
            FailExpiredGhostRoleObjective(key);
            return false;
        }

        _objectiveRuntimeByTarget.Remove(spawner);
        state.TargetEntity = target;
        state.GhostRoleTaken = true;
        state.GhostRoleAcceptDeadline = null;
        var runtime = EnsureContractRuntime(contract);
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        _objectiveRuntimeByTarget[target] = key;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (!TerminatingOrDeleted(pinpointer))
                _pinpointer.SetTarget(pinpointer, target);
        }

        var config = EnsureContractConfig(contract);
        if (config.GuardCount <= 0 || string.IsNullOrWhiteSpace(config.GuardPrototype))
            return true;

        if (!TryComp(target, out TransformComponent? targetXform))
            return true;

        if (TrySpawnObjectiveGuards(key, state, config, targetXform.Coordinates))
            return true;

        Sawmill.Warning($"[Contracts] Ghost role guard wave failed for '{key.ContractId}'.");
        return true;
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

        if (!contract.Taken || !contract.IsGhostRoleObjective || contract.Completed)
            return;

        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-timeout"),
            deleteGuards: false);
    }

    private void HandleGhostRoleTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-target-lost"),
            deleteGuards: false);
    }

    public bool HasRealtimeContractState(NcStoreComponent comp)
    {
        foreach (var contract in comp.Contracts.Values)
        {
            if (!contract.Taken)
                continue;

            EnsureObjectiveRuntimeDefaults(contract);
            if (EnsureContractRuntime(contract).Failed || contract.Completed)
                continue;

            if (contract.IsGhostRoleObjective ||
                contract.IsTrackedDeliveryObjective ||
                contract.AllowsStoreWorldTurnIn)
            {
                return true;
            }
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
        return (targetPos - storePos).LengthSquared() <=
            NcContractTuning.GhostRoleStoreDeliveryRange * NcContractTuning.GhostRoleStoreDeliveryRange;
    }

    private void SyncGhostRoleObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        var runtime = EnsureContractRuntime(contract);

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
        {
            runtime.GhostRolePendingAcceptance = false;
            runtime.AcceptTimeoutRemainingSeconds = 0;
            return;
        }

        if (!state.GhostRoleTaken && state.GhostRoleAcceptDeadline is { } deadline)
        {
            runtime.GhostRolePendingAcceptance = true;
            runtime.AcceptTimeoutRemainingSeconds = Math.Max(0, (int)Math.Ceiling((deadline - _timing.CurTime).TotalSeconds));
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
        runtime.Stage = state.GhostRoleTaken && isDead && IsGhostRoleTargetAtStore(store, target)
            ? Math.Max(1, runtime.StageGoal)
            : 0;
    }
}
