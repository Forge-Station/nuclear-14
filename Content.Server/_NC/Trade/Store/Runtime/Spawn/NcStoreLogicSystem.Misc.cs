using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


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

    public int TrySpawnProductUnits(string protoId, EntityUid user, int units)
    {
        if (units <= 0 || string.IsNullOrWhiteSpace(protoId) || !Exists(user))
            return 0;

        if (!_protos.TryIndex<EntityPrototype>(protoId, out var productProto))
            return 0;

        var stackComponentName = _compFactory.GetComponentName(typeof(StackComponent));

        if (productProto.TryGetComponent(stackComponentName, out StackComponent? stackComp))
        {
            var remaining = units;
            var spawnedTotal = 0;

            var stackTypeId = stackComp.StackTypeId;
            var maxPerStack = int.MaxValue;

            if (!string.IsNullOrWhiteSpace(stackTypeId) &&
                _protos.TryIndex<StackPrototype>(stackTypeId, out var stackTypeProto))
                maxPerStack = stackTypeProto.MaxCount ?? int.MaxValue;

            if (maxPerStack <= 0)
                maxPerStack = 1;

            var cachedItems = _inventory.GetOrBuildDeepItemsCacheCompacted(user);
            foreach (var ent in cachedItems)
            {
                if (remaining <= 0)
                    break;

                if (!TryComp(ent, out StackComponent? existingStack) ||
                    existingStack.StackTypeId != stackTypeId)
                    continue;

                var spaceLeft = maxPerStack - existingStack.Count;
                if (spaceLeft <= 0)
                    continue;

                var toAdd = Math.Min(spaceLeft, remaining);
                _stacks.SetCount(ent, existingStack.Count + toAdd, existingStack);

                remaining -= toAdd;
                spawnedTotal += toAdd;
            }

            if (remaining <= 0)
            {
                InvalidateInventoryCache(user);
                return spawnedTotal;
            }

            var userCoords = _ents.GetComponent<TransformComponent>(user).Coordinates;

            while (remaining > 0)
            {
                var chunk = Math.Min(remaining, maxPerStack);

                try
                {
                    var spawned = _ents.SpawnEntity(protoId, userCoords);

                    if (TryComp(spawned, out StackComponent? spawnedStack))
                        _stacks.SetCount(spawned, chunk, spawnedStack);

                    QueuePickupToHandsOrCrateNextTick(user, spawned);

                    spawnedTotal += chunk;
                    remaining -= chunk;
                }
                catch (Exception e)
                {
                    Sawmill.Error($"Spawn failed during unit spawning: {protoId} x{remaining}: {e}");
                    break;
                }
            }

            InvalidateInventoryCache(user);
            return spawnedTotal;
        }

        var ok = 0;
        for (var i = 0; i < units; i++)
            ok += TrySpawnProduct(protoId, user) ? 1 : 0;

        return ok;
    }
}
