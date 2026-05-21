using Content.Shared._NC.Trade;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
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

        if (!TryResolveTrackedObjectiveSpawnPrototype(
            contractId,
            contract,
            config.TargetPrototype,
            false,
            out var targetProtoId))
            return false;

        if (!TryInitializeTrackedTargetAndSupport(
            store,
            user,
            contractId,
            contract,
            targetProtoId,
            true))
            return false;

        config.TargetPrototype = targetProtoId;
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

        var requestedTargetId = ResolveTrackedObjectivePrototypeId(config.TargetPrototype, contract.TargetItem);
        if (!TryResolveTrackedObjectiveSpawnPrototype(
            contractId,
            contract,
            requestedTargetId,
            true,
            out var targetProtoId))
            return false;

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, targetProtoId))
            return false;

        config.TargetPrototype = targetProtoId;
        ResetObjectiveState(contract);

        return true;
    }

    private bool TryResolveTrackedObjectiveSpawnPrototype(
        string contractId,
        ContractServerData contract,
        string requestedTargetId,
        bool allowSpawnSpecific,
        out string resolvedTargetProtoId
    )
    {
        resolvedTargetProtoId = string.Empty;

        var config = contract.Config;
        if (allowSpawnSpecific &&
            TryPickTrackedObjectiveSpecificSpawnPrototype(config.SpawnSpecific, out var specificProto))
        {
            resolvedTargetProtoId = specificProto;
            return true;
        }

        if (contract.MatchMode == PrototypeMatchMode.Matcher)
        {
            if (TryPickMatcherSpawnPrototype(requestedTargetId, out var matcherProtoId))
            {
                resolvedTargetProtoId = matcherProtoId;
                return true;
            }

            if (_prototypes.HasIndex<EntityPrototype>(requestedTargetId))
            {
                resolvedTargetProtoId = requestedTargetId;
                return true;
            }

            Sawmill.Warning(
                $"[Contracts] Objective init failed for '{contractId}': matcher target '{requestedTargetId}' has no spawnable items.");
            return false;
        }

        resolvedTargetProtoId = requestedTargetId;
        return true;
    }

    private bool TryPickTrackedObjectiveSpecificSpawnPrototype(
        IReadOnlyList<string>? spawnSpecific,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (spawnSpecific is not { Count: > 0, })
            return false;

        for (var i = 0; i < spawnSpecific.Count; i++)
        {
            var candidate = spawnSpecific[i];
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (_prototypes.HasIndex<EntityPrototype>(candidate))
            {
                prototypeId = candidate;
                return true;
            }
        }

        return false;
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
        if (!TryValidateObjectiveTargetPrototype(contractId, targetProtoId))
            return false;

        var config = contract.Config;
        if (!TryResolveTrackedTargetSpawnCoordinates(store, contractId, config, out var spawnCoords))
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
        out EntityCoordinates spawnCoords
    )
    {
        if (TryResolveObjectiveSpawnCoordinates(store, config, out spawnCoords))
            return true;

        Sawmill.Warning($"[Contracts] Objective init failed for '{contractId}': cannot resolve spawn coordinates.");
        return false;
    }

    private bool TrySpawnObjectiveTarget(
        string contractId,
        string targetProtoId,
        EntityCoordinates spawnCoords,
        out EntityUid target
    )
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
        state.HuntTargetWasKilled = false;
        state.TargetEntity = target;

        if (TryComp(target, out TransformComponent? targetXform))
            state.LastKnownTargetCoordinates = targetXform.Coordinates;

        _objectiveRuntime.ByTarget[target] = key;
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
            DeactivateTrackedDeliveryDropoff((store, contractId), state);
            return true;
        }

        if (!TryResolveObjectiveDropoffCoordinates(store, config, out var dropoffCoords))
        {
            Sawmill.Warning(
                $"[Contracts] Objective init failed for '{contractId}': cannot resolve dropoff coordinates.");
            return false;
        }

        state.DeliveryDropoffCoordinates = _xform.ToMapCoordinates(dropoffCoords);
        if (!TrySpawnDeliveryDropoffMarker(contractId, state, dropoffCoords))
            return false;

        ActivateTrackedDeliveryDropoff((store, contractId), state);
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

    private bool TrySpawnDeliveryDropoffMarker(
        string contractId,
        ObjectiveRuntimeState state,
        EntityCoordinates dropoffCoords
    )
    {
        EntityUid dropoffMarker;
        try
        {
            dropoffMarker = Spawn(NcContractTuning.DefaultTrackedDeliveryDropoffSignPrototypeId, dropoffCoords);
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{contractId}': cannot spawn dropoff sign '{NcContractTuning.DefaultTrackedDeliveryDropoffSignPrototypeId}': {e}");
            return false;
        }

        state.DeliveryDropoffEntity = dropoffMarker;
        return true;
    }

    private void ActivateTrackedDeliveryDropoff((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        if (state.ActiveDeliveryDropoff)
            return;

        state.DeliveryDropoffCompleted = false;
        state.ActiveDeliveryDropoff = true;
        _objectiveRuntime.ActiveTrackedDeliveryDropoffObjectives.Add(key);
    }

    private void DeactivateTrackedDeliveryDropoff((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        if (state.ActiveDeliveryDropoff)
            state.ActiveDeliveryDropoff = false;
        _objectiveRuntime.ActiveTrackedDeliveryDropoffObjectives.Remove(key);

        state.DeliveryDropoffCoordinates = null;

        if (state.DeliveryDropoffEntity is { } dropoffMarker)
        {
            state.DeliveryDropoffEntity = null;

            if (!TerminatingOrDeleted(dropoffMarker))
                Del(dropoffMarker);
        }
    }
}
