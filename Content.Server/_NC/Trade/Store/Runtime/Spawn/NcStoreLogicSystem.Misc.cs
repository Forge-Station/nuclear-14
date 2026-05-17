using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private const int DefaultMaxStackFallback = 1000;
    private readonly List<EntityUid> _stackFillItemsScratch = new();

    public bool TrySpawnProduct(string protoId, EntityUid user) => TrySpawnProductInternal(protoId, user, true);

    private bool TrySpawnProductInternal(string protoId, EntityUid user, bool invalidateCache)
    {
        if (!_protos.HasIndex<EntityPrototype>(protoId))
        {
            Sawmill.Warning($"[NcStore] Prototype not found: {protoId}");
            return false;
        }

        if (!TryComp(user, out TransformComponent? xform))
            return false;

        try
        {
            var spawned = _ents.SpawnEntity(protoId, xform.Coordinates);

            QueuePickupToHandsOrCrateNextTick(user, spawned);

            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error($"[NcStore] Unexpected spawn failure for {protoId}: {e}");
            return false;
        }
    }

    public int TrySpawnProductUnits(string protoId, EntityUid user, int units)
    {
        if (units <= 0 || string.IsNullOrWhiteSpace(protoId) || !Exists(user))
            return 0;

        if (!_protos.TryIndex<EntityPrototype>(protoId, out var productProto))
            return 0;

        return _spawnService.SpawnRewardProduct(user, protoId, productProto, units);
    }

    #region Private Helpers

    private bool TryGetStackInfo(EntityPrototype proto, out string? stackTypeId, out int maxCount)
    {
        stackTypeId = null;
        maxCount = 1;

        if (!proto.TryGetComponent<StackComponent>(out var stackComp, _compFactory))
            return false;

        stackTypeId = stackComp.StackTypeId;

        if (string.IsNullOrWhiteSpace(stackTypeId) ||
            !_protos.TryIndex<StackPrototype>(stackTypeId, out var stackTypeProto))
            return false;

        maxCount = Math.Max(1, stackTypeProto.MaxCount ?? DefaultMaxStackFallback);
        return true;
    }

    private int FillExistingStacks(EntityUid user, string? stackTypeId, int maxCount, int toAdd)
    {
        var remaining = toAdd;
        var addedTotal = 0;
        _inventory.ScanInventoryItems(user, _stackFillItemsScratch);

        foreach (var ent in _stackFillItemsScratch)
        {
            if (remaining <= 0)
                break;
            if (!TryComp(ent, out StackComponent? stack) || stack.StackTypeId != stackTypeId)
                continue;

            var spaceLeft = maxCount - stack.Count;
            if (spaceLeft <= 0)
                continue;

            var amount = Math.Min(spaceLeft, remaining);
            _stacks.SetCount(ent, stack.Count + amount, stack);

            remaining -= amount;
            addedTotal += amount;
        }

        return addedTotal;
    }

    private int SpawnNewStackChunks(
        EntityCoordinates coords,
        EntityUid user,
        string protoId,
        int totalUnits,
        int maxCount
    )
    {
        var remaining = totalUnits;
        var spawnedTotal = 0;

        while (remaining > 0)
        {
            var chunkSize = Math.Min(remaining, maxCount);
            try
            {
                var spawned = _ents.SpawnEntity(protoId, coords);
                if (TryComp(spawned, out StackComponent? stack))
                    _stacks.SetCount(spawned, chunkSize, stack);

                QueuePickupToHandsOrCrateNextTick(user, spawned);

                spawnedTotal += chunkSize;
                remaining -= chunkSize;
            }
            catch (Exception e)
            {
                Sawmill.Error($"[NcStore] Chunk spawn failed: {protoId} x{chunkSize}: {e}");
                break;
            }
        }

        return spawnedTotal;
    }

    private int SpawnNonStackable(string protoId, EntityUid user, int units)
    {
        var count = 0;
        for (var i = 0; i < units; i++)
            if (TrySpawnProductInternal(protoId, user, false))
                count++;

        if (count > 0)
            _inventory.InvalidateInventoryCache(user);

        return count;
    }

    #endregion
}
