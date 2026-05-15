using Content.Shared._NC.Trade;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly List<EntityUid> _inventoryDeliverySpawnedScratch = new();

    private bool TryInitializeInventoryDeliverySupportRuntime(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var config = contract.Config;

        if (!TryInitializeRetrievalSpawnRuntime(store, user, contractId, contract))
            return false;

        if (!TryInitializeRetrievalRouteDeliveryRuntime(store, contractId, contract))
            return false;

        if (config.SpawnItems && contract.MatchMode != PrototypeMatchMode.Matcher)
        {
            Sawmill.Warning(
                $"[Contracts] Delivery support: contract '{contractId}' has spawnItems=true but match mode is {contract.MatchMode}. " +
                "spawnItems is matcher-only and will be ignored.");
        }

        var shouldSpawnDeliveryItems = config.SpawnItems && contract.MatchMode == PrototypeMatchMode.Matcher;
        var spawnProtoId = config.DeliverySpawnPrototype;
        var hasHelperSpawn = !string.IsNullOrWhiteSpace(spawnProtoId);

        if (!hasHelperSpawn && !shouldSpawnDeliveryItems)
            return true;

        if (hasHelperSpawn && !TryValidateInventoryDeliverySupportPrototype(contractId, spawnProtoId))
            return false;

        if (!TryResolveInventoryDeliverySupportCoordinates(store, contractId, config, out var spawnCoords))
            return false;

        var key = (store, contractId);
        if (!TryInitializeInventoryDeliverySupportGuards(key, config, spawnCoords))
            return false;

        _inventoryDeliverySpawnedScratch.Clear();

        if (hasHelperSpawn &&
            !TrySpawnInventoryDeliverySupportEntity(key, spawnProtoId, spawnCoords, _inventoryDeliverySpawnedScratch))
        {
            CleanupInventoryDeliverySpawnedEntities(_inventoryDeliverySpawnedScratch);
            return false;
        }

        if (shouldSpawnDeliveryItems &&
            !TrySpawnInventoryDeliveryContractItems(key, contract, spawnCoords, _inventoryDeliverySpawnedScratch))
        {
            CleanupInventoryDeliverySpawnedEntities(_inventoryDeliverySpawnedScratch);
            return false;
        }

        _inventoryDeliverySpawnedScratch.Clear();
        return true;
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
        EntityCoordinates spawnCoords,
        List<EntityUid> spawnedEntities)
    {
        try
        {
            var spawned = Spawn(spawnProtoId, spawnCoords);
            spawnedEntities.Add(spawned);
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

    private bool TrySpawnInventoryDeliveryContractItems(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        EntityCoordinates spawnCoords,
        List<EntityUid> spawnedEntities)
    {
        if (!TryBuildInventoryDeliverySpawnQueue(contract, out var queue))
            return false;

        if (queue.Count == 0)
            return true;

        for (var i = 0; i < queue.Count; i++)
        {
            var protoId = queue[i];
            try
            {
                var spawned = Spawn(protoId, spawnCoords);
                spawnedEntities.Add(spawned);
            }
            catch (Exception e)
            {
                CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities: false);
                Sawmill.Error(
                    $"[Contracts] Delivery support init failed for '{key.ContractId}': cannot spawn delivery item '{protoId}': {e}");
                return false;
            }
        }

        return true;
    }

    private bool TryBuildInventoryDeliverySpawnQueue(ContractServerData contract, out List<string> queue)
    {
        queue = new List<string>();

        var targets = GetEffectiveTargets(contract);
        var requirements = new List<(string MatcherId, int Required)>();
        var totalRequired = 0;

        if (targets.Count > 0)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.Required <= 0 || string.IsNullOrWhiteSpace(target.TargetItem))
                    continue;

                requirements.Add((target.TargetItem, target.Required));
                totalRequired = SaturatingAdd(totalRequired, target.Required);
            }
        }

        if (requirements.Count == 0 &&
            contract.Required > 0 &&
            !string.IsNullOrWhiteSpace(contract.TargetItem))
        {
            requirements.Add((contract.TargetItem, contract.Required));
            totalRequired = contract.Required;
        }

        if (requirements.Count == 0 || totalRequired <= 0)
            return true;

        if (!AppendDeliverySpawnSpecific(queue, contract.Config.SpawnSpecific, totalRequired))
            return false;

        var remaining = totalRequired - queue.Count;
        if (remaining <= 0)
            return true;

        for (var i = 0; i < requirements.Count && remaining > 0; i++)
        {
            var (matcherId, required) = requirements[i];
            if (!TryGetContractMatcherSpec(matcherId, out var matcherSpec))
            {
                Sawmill.Warning(
                    $"[Contracts] Delivery support: matcher '{matcherId}' cannot be resolved for spawnItems. " +
                    "Falling back to manual delivery for remaining amount.");
                return true;
            }

            if (matcherSpec.SpawnPool.Count == 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Delivery support: matcher '{matcherId}' has no spawnable items. " +
                    "Falling back to manual delivery for remaining amount.");
                return true;
            }

            var toFill = Math.Min(required, remaining);
            for (var j = 0; j < toFill; j++)
                queue.Add(_random.Pick(matcherSpec.SpawnPool));

            remaining -= toFill;
        }

        if (remaining > 0 && requirements.Count > 0)
        {
            var fallbackMatcher = requirements[0].MatcherId;
            if (!TryGetContractMatcherSpec(fallbackMatcher, out var fallbackSpec) || fallbackSpec.SpawnPool.Count == 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Delivery support: cannot fill remaining spawn slots from matcher '{fallbackMatcher}'. " +
                    "Falling back to manual delivery for remaining amount.");
                return true;
            }

            for (var i = 0; i < remaining; i++)
                queue.Add(_random.Pick(fallbackSpec.SpawnPool));
        }

        return true;
    }

    private bool AppendDeliverySpawnSpecific(List<string> queue, List<string>? spawnSpecific, int maxCount)
    {
        if (spawnSpecific is not { Count: > 0 } || maxCount <= 0)
            return true;

        for (var i = 0; i < spawnSpecific.Count && queue.Count < maxCount; i++)
        {
            var protoId = spawnSpecific[i];
            if (string.IsNullOrWhiteSpace(protoId))
                continue;

            if (!_prototypes.HasIndex<EntityPrototype>(protoId))
            {
                Sawmill.Warning($"[Contracts] spawnSpecific contains missing prototype '{protoId}' (ignored).");
                continue;
            }

            queue.Add(protoId);
        }

        return true;
    }

    private void CleanupInventoryDeliverySpawnedEntities(List<EntityUid> spawnedEntities)
    {
        for (var i = spawnedEntities.Count - 1; i >= 0; i--)
        {
            var ent = spawnedEntities[i];
            if (ent != EntityUid.Invalid && !TerminatingOrDeleted(ent))
                Del(ent);
        }

        spawnedEntities.Clear();
    }
}
