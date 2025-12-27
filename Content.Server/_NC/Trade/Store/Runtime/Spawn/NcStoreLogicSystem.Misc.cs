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

            var pickedUp = false;
            if (_ents.HasComponent<HandsComponent>(user))
                pickedUp = _hands.TryPickupAnyHand(user, spawned, false);

            if (!pickedUp && TryGetPulledClosedCrate(user, out var crate) && Exists(crate))
            {
                _entityStorage.Insert(spawned, crate);
                InvalidateInventoryCache(crate);
            }

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

            var available = GetOwnedInRoot(root, protoId, PrototypeMatchMode.Exact);
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
                list = new List<(string ProtoId, int Amount)>();
                grouped[root] = list;
            }

            list.Add((protoId, amount));
        }

        foreach (var (root, reqs) in grouped)
        {
            var cachedItems = GetOrBuildDeepItemsCacheCompacted(root);

            for (var i = 0; i < reqs.Count; i++)
            {
                var (protoId, amount) = reqs[i];
                if (!TryTakeProductUnitsFromCachedList(root, cachedItems, protoId, amount, PrototypeMatchMode.Exact))
                {
                    Sawmill.Error(
                        $"[NcStore] ExecuteContractBatch CRITICAL: Validation passed but take failed for {amount} of {protoId} from {ToPrettyString(root)}.");
                    InvalidateInventoryCache(root);
                    return false;
                }
            }

            CompactCachedItems(cachedItems);
            InvalidateInventoryCache(root);
        }

        return true;
    }
}
