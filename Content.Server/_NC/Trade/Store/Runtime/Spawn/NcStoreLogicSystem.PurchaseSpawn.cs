using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{

    private int SpawnPurchasedProduct(
        EntityUid user,
        string productEntity,
        EntityPrototype productProto,
        int amount,
        int unitPrice,
        string currency
    )
    {
        if (amount <= 0)
            return 0;

        var spawnedTotal = 0;

        if (productProto.TryGetComponent("Stack", out StackComponent? stackComp))
        {
            var userCoords = _ents.GetComponent<TransformComponent>(user).Coordinates;

            var maxPerStack = int.MaxValue;
            if (!string.IsNullOrWhiteSpace(stackComp.StackTypeId) &&
                _protos.TryIndex<StackPrototype>(stackComp.StackTypeId, out var stackTypeProto))
                maxPerStack = stackTypeProto.MaxCount ?? int.MaxValue;

            if (maxPerStack <= 0)
                maxPerStack = 1;

            var remainingToSpawn = amount;

            while (remainingToSpawn > 0)
            {
                var chunk = Math.Min(remainingToSpawn, maxPerStack);

                try
                {
                    var spawned = _ents.SpawnEntity(productEntity, userCoords);

                    if (_ents.TryGetComponent(spawned, out StackComponent? spawnedStack))
                        _stacks.SetCount(spawned, chunk, spawnedStack);

                    var pickedUp = false;
                    if (_ents.HasComponent<HandsComponent>(user))
                        pickedUp = _hands.TryPickupAnyHand(user, spawned, false);

                    if (!pickedUp && TryGetPulledClosedCrate(user, out var crate) && Exists(crate))
                    {
                        _entityStorage.Insert(spawned, crate);
                        InvalidateInventoryCache(crate);
                    }

                    spawnedTotal += chunk;
                    remainingToSpawn -= chunk;
                }
                catch (Exception e)
                {
                    Sawmill.Error($"Spawn failed during bulk buy: {e}");

                    if (remainingToSpawn > 0)
                    {
                        var refundL = (long) remainingToSpawn * unitPrice;
                        if (refundL > 0 && refundL <= int.MaxValue)
                            GiveCurrency(user, currency, (int) refundL);
                    }

                    break;
                }
            }

            return spawnedTotal;
        }

        for (var i = 0; i < amount; i++)
            if (TrySpawnProduct(productEntity, user))
                spawnedTotal++;
            else
                GiveCurrency(user, currency, unitPrice);

        return spawnedTotal;
    }
}
