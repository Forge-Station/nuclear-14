using Content.Shared._NC.Trade;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly List<string> _retrievalSpawnQueueScratch = new();

    private bool TryInitializeRetrievalSpawnRuntime(
        EntityUid store,
        EntityUid user,
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

        if (ShouldAutoIssueRetrievalRoutePinpointer(contract) &&
            !TryIssueInitialRetrievalRoutePinpointer(user, key, state, contract, spawnCoords))
        {
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
            return false;
        }

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
                $"[Contracts] Retrieval route init failed for '{contractId}': source point is missing.");
            return false;
        }

        if (config.RetrievalSpawnPoint.Type == ContractPointSelectorType.Store)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval route init failed for '{contractId}': Store source point is not valid.");
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
            $"[Contracts] Retrieval route init failed for '{contractId}': cannot resolve source marker.");
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
                    $"[Contracts] Retrieval route init failed for '{contractId}': unsupported cargo match mode {target.MatchMode}.");
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
                $"[Contracts] Retrieval route init failed for '{contractId}': cargo prototype '{target.TargetItem}' is missing.");
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
                $"[Contracts] Retrieval route init failed for '{contractId}': cargo group '{target.TargetItem}' has no spawnable prototypes.");
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
            RegisterRetrievalSpawnedCargo(key, state, spawned);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Retrieval route init failed for '{key.ContractId}': cannot spawn cargo '{protoId}': {e}");
            return false;
        }
    }

    private static bool ShouldAutoIssueRetrievalRoutePinpointer(ContractServerData contract)
    {
        var config = contract.Config;
        return UsesRetrievalSpawnedCargoSupport(contract) &&
               config.RetrievalGuidancePinpointerEnabled &&
               config.RetrievalGuidancePinpointerTarget == NcRetrievalPinpointerTargetMode.CargoThenDestinationThenStore;
    }

    private bool TryIssueInitialRetrievalRoutePinpointer(
        EntityUid user,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractServerData contract,
        EntityCoordinates spawnCoords)
    {
        if (!TryResolveRetrievalSpawnedPinpointerTarget(key.Store, contract, state, out var target))
            return false;

        return TrySpawnObjectivePinpointer(user, target, key, state, contract.Config, spawnCoords);
    }
}
