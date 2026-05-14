using Content.Shared._NC.Trade;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly List<string> _retrievalSpawnQueueScratch = new();

    private bool TryInitializeRetrievalSpawnRuntime(
        EntityUid store,
        string contractId,
        ContractServerData contract)
    {
        var config = contract.Config;
        if (!config.RetrievalSpawnEnabled)
            return true;

        if (!TryResolveRetrievalSpawnCoordinates(store, contractId, config, out var spawnCoords))
            return false;

        _retrievalSpawnQueueScratch.Clear();
        if (!TryBuildRetrievalSpawnQueue(contractId, contract, _retrievalSpawnQueueScratch))
        {
            _retrievalSpawnQueueScratch.Clear();
            return false;
        }

        if (_retrievalSpawnQueueScratch.Count == 0)
            return true;

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);

        for (var i = 0; i < _retrievalSpawnQueueScratch.Count; i++)
        {
            var protoId = _retrievalSpawnQueueScratch[i];
            if (TrySpawnRetrievalTargetItem(key, state, protoId, spawnCoords))
                continue;

            _retrievalSpawnQueueScratch.Clear();
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
            return false;
        }

        _retrievalSpawnQueueScratch.Clear();
        return true;
    }

    private bool TryResolveRetrievalSpawnCoordinates(
        EntityUid store,
        string contractId,
        ContractObjectiveConfigData config,
        out EntityCoordinates spawnCoords)
    {
        spawnCoords = EntityCoordinates.Invalid;

        if (config.RetrievalSpawnPoint == null)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval spawn init failed for '{contractId}': spawn point is missing.");
            return false;
        }

        if (config.RetrievalSpawnPoint.Type == ContractPointSelectorType.Store)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval spawn init failed for '{contractId}': Store spawn point is not valid for Retrieval V2 Stage 2.");
            return false;
        }

        if (TryResolveObjectiveSpawnCoordinates(
                store,
                config.RetrievalSpawnPoint,
                out spawnCoords,
                fallbackToStore: config.RetrievalSpawnFallbackToStore))
        {
            return true;
        }

        Sawmill.Warning(
            $"[ContractsV2] Retrieval spawn init failed for '{contractId}': cannot resolve spawn marker.");
        return false;
    }

    private bool TryBuildRetrievalSpawnQueue(
        string contractId,
        ContractServerData contract,
        List<string> queue)
    {
        queue.Clear();

        var targets = GetEffectiveTargets(contract);
        if (targets.Count > 0)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (!TryAppendRetrievalSpawnTarget(contractId, targets[i], queue))
                    return false;
            }

            return true;
        }

        if (contract.Required <= 0 || string.IsNullOrWhiteSpace(contract.TargetItem))
            return true;

        return TryAppendRetrievalSpawnTarget(
            contractId,
            new ContractTargetServerData
            {
                TargetItem = contract.TargetItem,
                Required = contract.Required,
                MatchMode = contract.MatchMode
            },
            queue);
    }

    private bool TryAppendRetrievalSpawnTarget(
        string contractId,
        ContractTargetServerData target,
        List<string> queue)
    {
        if (target.Required <= 0 || string.IsNullOrWhiteSpace(target.TargetItem))
            return true;

        switch (target.MatchMode)
        {
            case PrototypeMatchMode.Exact:
                return TryAppendExactRetrievalSpawnTarget(contractId, target, queue);

            case PrototypeMatchMode.Matcher:
                return TryAppendMatcherRetrievalSpawnTarget(contractId, target, queue);

            default:
                Sawmill.Warning(
                    $"[ContractsV2] Retrieval spawn init failed for '{contractId}': unsupported target match mode {target.MatchMode}.");
                return false;
        }
    }

    private bool TryAppendExactRetrievalSpawnTarget(
        string contractId,
        ContractTargetServerData target,
        List<string> queue)
    {
        if (!_prototypes.HasIndex<EntityPrototype>(target.TargetItem))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval spawn init failed for '{contractId}': target prototype '{target.TargetItem}' is missing.");
            return false;
        }

        for (var i = 0; i < target.Required; i++)
            queue.Add(target.TargetItem);

        return true;
    }

    private bool TryAppendMatcherRetrievalSpawnTarget(
        string contractId,
        ContractTargetServerData target,
        List<string> queue)
    {
        for (var i = 0; i < target.Required; i++)
        {
            if (TryPickMatcherSpawnPrototype(target.TargetItem, out var protoId))
            {
                queue.Add(protoId);
                continue;
            }

            Sawmill.Warning(
                $"[ContractsV2] Retrieval spawn init failed for '{contractId}': target group/matcher '{target.TargetItem}' has no spawnable prototypes.");
            return false;
        }

        return true;
    }

    private bool TrySpawnRetrievalTargetItem(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        string protoId,
        EntityCoordinates spawnCoords)
    {
        try
        {
            var spawned = Spawn(protoId, spawnCoords);
            state.RetrievalSpawnedEntities.Add(spawned);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[ContractsV2] Retrieval spawn init failed for '{key.ContractId}': cannot spawn '{protoId}': {e}");
            return false;
        }
    }
}
