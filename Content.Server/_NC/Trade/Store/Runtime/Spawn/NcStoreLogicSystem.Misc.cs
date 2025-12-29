using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    public bool TrySpawnProduct(string protoId, EntityUid user)
    {
        try
        {
            var userCoords = _ents.GetComponent<TransformComponent>(user).Coordinates;
            var spawned = _ents.SpawnEntity(protoId, userCoords);

            QueuePickupToHandsOrCrateNextTick(user, spawned);

            InvalidateInventoryCache(user);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error($"Spawn failed for {protoId}: {e}");
            return false;
        }
    }

    public bool ExecuteContractBatch(Dictionary<(EntityUid Root, string ProtoId), int> plan)
    {
        foreach (var ((root, protoId), amount) in plan)
        {
            if (amount <= 0)
                continue;
            var snap = _inventory.BuildInventorySnapshot(root);
            var available = _inventory.GetOwnedFromSnapshot(snap, protoId, PrototypeMatchMode.Exact);

            if (available < amount)
            {
                Sawmill.Warning(
                    $"[NcStore] ExecuteContractBatch dry-run failed: {ToPrettyString(root)} has {available} of {protoId}, needed {amount}. Aborting transaction.");
                return false;
            }
        }
        var grouped = new Dictionary<EntityUid, List<(string ProtoId, int Amount)>>();
        foreach (var ((root, protoId), amount) in plan)
        {
            if (amount <= 0)
                continue;

            if (!grouped.TryGetValue(root, out var list))
            {
                list = new();
                grouped[root] = list;
            }

            list.Add((protoId, amount));
        }

        foreach (var (root, reqs) in grouped)
        {
            var cachedItems = _inventory.GetOrBuildDeepItemsCacheCompacted(root);

            for (var i = 0; i < reqs.Count; i++)
            {
                var (protoId, amount) = reqs[i];
                if (!_inventory.TryTakeProductUnitsFromCachedList(
                    root,
                    cachedItems,
                    protoId,
                    amount,
                    PrototypeMatchMode.Exact))
                {
                    Sawmill.Error(
                        $"[NcStore] ExecuteContractBatch CRITICAL: Validation passed but take failed for {amount} of {protoId} from {ToPrettyString(root)}.");
                    _inventory.InvalidateInventoryCache(root);
                    return false;
                }
            }

            _inventory.InvalidateInventoryCache(root);
        }

        return true;
    }
}
