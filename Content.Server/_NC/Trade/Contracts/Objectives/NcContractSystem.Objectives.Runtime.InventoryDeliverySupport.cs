using Content.Shared._NC.Trade;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
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
}
