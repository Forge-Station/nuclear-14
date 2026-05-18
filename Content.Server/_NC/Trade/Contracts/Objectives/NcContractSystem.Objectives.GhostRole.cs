using Content.Server.Atmos.Rotting;
using Content.Server.Cuffs;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind.Commands;
using Content.Shared._NC.Trade;
using Content.Shared.Cuffs.Components;
using Content.Shared.Customization.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mind.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly RottingSystem _contractGhostRoleRotting = default!;
    [Dependency] private readonly CuffableSystem _contractGhostRoleCuffs = default!;

    private bool TryInitializeGhostRoleObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        if (!TryResolveGhostRolePrototype(contractId, contract, out var ghostRoleProtoId))
            return false;

        var config = contract.Config;
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
        out string ghostRoleProtoId
    )
    {
        ghostRoleProtoId = ResolveTrackedObjectivePrototypeId(
            contract.Config.GhostRolePrototype,
            contract.TargetItem);
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
        out EntityCoordinates spawnCoords
    )
    {
        if (TryResolveObjectiveSpawnCoordinates(store, config, out spawnCoords))
            return true;

        Sawmill.Warning($"[Contracts] Ghost role init failed for '{contractId}': cannot resolve spawn coordinates.");
        return false;
    }

    private bool TrySpawnGhostRoleSpawner(
        string contractId,
        EntityCoordinates spawnCoords,
        out EntityUid spawner
    )
    {
        try
        {
            spawner = Spawn(null, spawnCoords);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Ghost role init failed for '{contractId}': runtime spawner creation threw: {e}");
            spawner = EntityUid.Invalid;
            return false;
        }
    }

    private void ConfigureGhostRoleSpawner(EntityUid spawner, ContractServerData contract, string ghostRoleProtoId)
    {
        var config = contract.Config;
        var ghostRole = EnsureComp<GhostRoleComponent>(spawner);
        ghostRole.RoleName = ResolveContractGhostRoleName(config, contract);
        ghostRole.RoleDescription = ResolveContractGhostRoleDescription(config, contract);
        ghostRole.RoleRules = ResolveContractGhostRoleRules(config);

        var spawnerComp = EnsureComp<NcContractGhostRoleSpawnerComponent>(spawner);
        spawnerComp.TargetPrototype = ghostRoleProtoId;
        spawnerComp.Requirements = config.GhostRoleRequirements.Count > 0
            ? new(config.GhostRoleRequirements)
            : new List<CharacterRequirement>();
    }

    private static string
        ResolveContractGhostRoleName(ContractObjectiveConfigData config, ContractServerData contract) =>
        string.IsNullOrWhiteSpace(config.GhostRoleName)
            ? contract.Name
            : config.GhostRoleName;

    private static string ResolveContractGhostRoleDescription(
        ContractObjectiveConfigData config,
        ContractServerData contract
    ) =>
        string.IsNullOrWhiteSpace(config.GhostRoleDescription)
            ? contract.Description
            : config.GhostRoleDescription;

    private static string ResolveContractGhostRoleRules(ContractObjectiveConfigData config) =>
        string.IsNullOrWhiteSpace(config.GhostRoleRules)
            ? "ghost-role-component-default-rules"
            : config.GhostRoleRules;

    private void RegisterGhostRoleObjectiveState(
        (EntityUid Store, string ContractId) key,
        EntityUid spawner,
        ContractServerData contract
    )
    {
        var config = contract.Config;
        var runtime = contract.Runtime;
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

    private void OnContractGhostRoleTakeover(
        EntityUid uid,
        NcContractGhostRoleSpawnerComponent comp,
        ref TakeGhostRoleEvent args
    )
    {
        if (!TryComp(uid, out GhostRoleComponent? ghostRole) ||
            comp.Claimed ||
            !CanTakeContractGhostRole(args.Player, uid, comp, ghostRole))
        {
            args.TookRole = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(comp.TargetPrototype) ||
            !_prototypes.HasIndex<EntityPrototype>(comp.TargetPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Ghost role take failed for {ToPrettyString(uid)}: invalid prototype '{comp.TargetPrototype}'.");
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
            contract.Runtime.Failed)
            return false;

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
        var runtime = contract.Runtime;
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        _objectiveRuntimeByTarget[target] = key;

        RetargetObjectivePinpointers(key, state, target);
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
            if (state.GhostRoleTaken)
            {
                if (TryGetObjectiveContract(key, out var comp, out var contract) &&
                    TryFailGhostRoleTargetIfInvalidOrRotten(key, state, comp, contract))
                {
                    continue;
                }

                TryRetargetGhostRolePinpointersForOwners(key, state);
                continue;
            }

            if (state.GhostRoleAcceptDeadline is not { } deadline)
                continue;

            if (_timing.CurTime >= deadline)
                _objectiveRuntimeKeysScratch.Add(key);
        }

        for (var i = 0; i < _objectiveRuntimeKeysScratch.Count; i++)
            FailExpiredGhostRoleObjective(_objectiveRuntimeKeysScratch[i]);

        _objectiveRuntimeKeysScratch.Clear();
    }

    private bool TryRetargetGhostRolePinpointersForOwners(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state)
    {
        if (state.PinpointerEntities.Count == 0)
            return false;

        if (!TryGetObjectiveContract(key, out _, out var contract) ||
            !contract.Taken ||
            contract.Runtime.Failed ||
            !contract.IsGhostRoleObjective ||
            !state.GhostRoleTaken)
        {
            return false;
        }

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return true;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            if (!_objectiveRuntimePinpointerOwners.TryGetValue(pinpointer, out var owner) ||
                !TryResolveGhostRolePinpointerTargetForUser(key.Store, owner, contract, state, out var target) ||
                target == EntityUid.Invalid ||
                TerminatingOrDeleted(target))
            {
                continue;
            }

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }

        return true;
    }

    private bool TryResolveGhostRolePinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!contract.IsGhostRoleObjective || !state.GhostRoleTaken)
            return false;

        if (state.TargetEntity is not { } tracked ||
            tracked == EntityUid.Invalid ||
            TerminatingOrDeleted(tracked))
        {
            return false;
        }

        if (IsGhostRoleTargetReadyForClaim(store, tracked, contract) ||
            IsGhostRoleTargetCarriedByUser(tracked, user))
        {
            target = store;
            return true;
        }

        target = tracked;
        return true;
    }

    private void FailExpiredGhostRoleObjective((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            state.GhostRoleTaken ||
            state.GhostRoleAcceptDeadline is not { } deadline ||
            _timing.CurTime < deadline)
            return;

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
            Loc.GetString("nc-store-contract-ghost-role-timeout"));
    }

    private void HandleGhostRoleTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    ) =>
        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-target-lost"));

    public bool HasRealtimeContractState(NcStoreComponent comp)
    {
        foreach (var contract in comp.Contracts.Values)
        {
            if (!contract.Taken)
                continue;

            EnsureObjectiveRuntimeDefaults(contract);
            if (contract.Runtime.Failed || contract.Completed)
                continue;

            if (contract.IsGhostRoleObjective ||
                contract.IsTrackedDeliveryObjective ||
                contract.AllowsStoreWorldTurnIn)
                return true;
        }

        return false;
    }

    private bool IsGhostRoleTargetAtStore(EntityUid store, EntityUid target)
    {
        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(target, out TransformComponent? targetXform))
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
            runtime.AcceptTimeoutRemainingSeconds = Math.Max(
                0,
                (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds));
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

        if (TryGetObjectiveContract(key, out var comp, out var liveContract) &&
            TryFailGhostRoleTargetIfInvalidOrRotten(key, state, comp, liveContract))
        {
            return;
        }

        runtime.Stage = state.GhostRoleTaken && IsGhostRoleCompletionSatisfied(store, target, contract)
            ? Math.Max(1, runtime.StageGoal)
            : 0;

        TryRetargetGhostRolePinpointersForOwners(key, state);
    }

    private bool TryFailGhostRoleTargetIfInvalidOrRotten(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        NcStoreComponent comp,
        ContractServerData contract)
    {
        if (!state.GhostRoleTaken ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid)
        {
            return false;
        }

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return true;
        }

        if (!_contractGhostRoleRotting.IsRotten(target))
            return false;

        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-target-rotten"));
        return true;
    }

    private bool IsGhostRoleTargetReadyForClaim(EntityUid store, EntityUid target, ContractServerData contract)
    {
        return !TerminatingOrDeleted(target) &&
               !_contractGhostRoleRotting.IsRotten(target) &&
               IsGhostRoleCompletionSatisfied(store, target, contract);
    }

    private bool IsGhostRoleCompletionSatisfied(EntityUid store, EntityUid target, ContractServerData contract)
    {
        if (!IsGhostRoleTargetAtStore(store, target))
            return false;

        return contract.Config.GhostRoleCompletionMode switch
        {
            NcGhostRoleCompletionMode.DeadBodyTurnIn => IsGhostRoleTargetDead(target),
            NcGhostRoleCompletionMode.AliveCuffedTurnIn => IsGhostRoleTargetAlive(target) &&
                                                           IsGhostRoleTargetCuffed(target) &&
                                                           IsGhostRoleTargetFullyHealed(target),
            _ => false
        };
    }

    private bool IsGhostRoleTargetDead(EntityUid target)
    {
        return TryComp(target, out MobStateComponent? mobState) &&
               mobState.CurrentState == MobState.Dead;
    }

    private bool IsGhostRoleTargetAlive(EntityUid target)
    {
        return TryComp(target, out MobStateComponent? mobState) &&
               mobState.CurrentState != MobState.Dead;
    }

    private bool IsGhostRoleTargetCuffed(EntityUid target)
    {
        return TryComp(target, out CuffableComponent? cuffable) &&
               _contractGhostRoleCuffs.IsCuffed((target, cuffable));
    }

    private bool IsGhostRoleTargetFullyHealed(EntityUid target)
    {
        return TryComp(target, out DamageableComponent? damageable) &&
               damageable.TotalDamage <= FixedPoint2.Zero;
    }

    private bool IsGhostRoleTargetCarriedByUser(EntityUid target, EntityUid user)
    {
        if (TryComp(target, out PullableComponent? pullable) && pullable.Puller == user)
            return true;

        return TryGetContainedEntityRoot(target, out var root) && root == user;
    }
}
