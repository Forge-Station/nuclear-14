using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind.Commands;
using Content.Shared._NC.Trade;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
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
        var config = contract.Config;
        var runtime = contract.Runtime;
        var ghostRoleProtoId = ResolveTrackedObjectivePrototypeId(config.GhostRolePrototype, contract.TargetItem);
        if (string.IsNullOrWhiteSpace(ghostRoleProtoId) || !_prototypes.HasIndex<EntityPrototype>(ghostRoleProtoId))
        {
            Sawmill.Warning($"[Contracts] Ghost role init failed for '{contractId}': ghost role prototype '{ghostRoleProtoId}' is missing.");
            return false;
        }

        config.GhostRolePrototype = ghostRoleProtoId;
        ResetObjectiveState(contract);

        if (!TryResolveObjectiveSpawnCoordinates(store, config.SpawnPointTag, out var spawnCoords))
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
        state.GhostRoleAcceptDeadline = config.AcceptTimeoutSeconds > 0
            ? _timing.CurTime + TimeSpan.FromSeconds(config.AcceptTimeoutSeconds)
            : null;
        _objectiveRuntimeByTarget[spawner] = key;

        runtime.GhostRolePendingAcceptance = state.GhostRoleAcceptDeadline != null;
        runtime.AcceptTimeoutRemainingSeconds = runtime.GhostRolePendingAcceptance
            ? Math.Max(0, config.AcceptTimeoutSeconds)
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
            contract.Completed ||
            contract.ObjectiveType != ContractObjectiveType.GhostRole ||
            contract.Runtime.Failed)
        {
            return false;
        }

        if (!state.GhostRoleTaken && state.GhostRoleAcceptDeadline is { } deadline && _timing.CurTime >= deadline)
        {
            FailExpiredGhostRoleObjective(key);
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

        if (contract.Config.GuardCount <= 0 || string.IsNullOrWhiteSpace(contract.Config.GuardPrototype))
            return true;

        if (!TryComp(target, out TransformComponent? targetXform))
            return true;

        if (TrySpawnObjectiveGuards(key, state, contract.Config, targetXform.Coordinates))
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

        if (!contract.Taken || contract.ObjectiveType != ContractObjectiveType.GhostRole || contract.Completed)
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
        return (targetPos - storePos).LengthSquared() <=
            NcContractTuning.GhostRoleStoreDeliveryRange * NcContractTuning.GhostRoleStoreDeliveryRange;
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
        contract.Runtime.Stage = state.GhostRoleTaken && isDead && IsGhostRoleTargetAtStore(store, target)
            ? Math.Max(1, contract.Runtime.StageGoal)
            : 0;
    }
}
