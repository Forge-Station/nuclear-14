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
}
