using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private StoreSpawnService _spawnService = default!;

    private void InitializeServices() => _spawnService = new(this);

    public bool TryPickCurrencyForBuy(
        NcStoreComponent store,
        StoreListingPrototype listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    ) =>
        _currency.TryPickCurrencyForBuy(store, listing, snapshot, out currency, out unitPrice, out balance);

    public bool TryPickCurrencyForSell(
        NcStoreComponent store,
        StoreListingPrototype listing,
        out string currency,
        out int unitPrice
    ) =>
        _currency.TryPickCurrencyForSell(store, listing, out currency, out unitPrice);

    private bool TryTakeCurrency(EntityUid user, string stackType, int amount) =>
        _currency.TryTakeCurrency(user, stackType, amount);

    public void GiveCurrency(EntityUid user, string stackType, int amount) =>
        _currency.GiveCurrency(user, stackType, amount);



    private sealed class StoreSpawnService
    {
        private readonly NcStoreLogicSystem _sys;
        public StoreSpawnService(NcStoreLogicSystem sys) { _sys = sys; }

        public int SpawnPurchasedProduct(
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
                var userCoords = _sys._ents.GetComponent<TransformComponent>(user).Coordinates;
                var maxPerStack = int.MaxValue;
                if (!string.IsNullOrWhiteSpace(stackComp.StackTypeId) &&
                    _sys._protos.TryIndex<StackPrototype>(stackComp.StackTypeId, out var stackTypeProto))
                    maxPerStack = stackTypeProto.MaxCount ?? int.MaxValue;
                if (maxPerStack <= 0)
                    maxPerStack = 1;

                var remainingToSpawn = amount;
                while (remainingToSpawn > 0)
                {
                    var chunk = Math.Min(remainingToSpawn, maxPerStack);
                    try
                    {
                        var spawned = _sys._ents.SpawnEntity(productEntity, userCoords);
                        if (_sys._ents.TryGetComponent(spawned, out StackComponent? spawnedStack))
                            _sys._stacks.SetCount(spawned, chunk, spawnedStack);

                        var pickedUp = false;
                        if (_sys._ents.HasComponent<HandsComponent>(user))
                            pickedUp = _sys._hands.TryPickupAnyHand(user, spawned, false);

                        if (!pickedUp && _sys.TryGetPulledClosedCrate(user, out var crate) && _sys.Exists(crate))
                        {
                            _sys._entityStorage.Insert(spawned, crate);
                            _sys._inventory.InvalidateInventoryCache(crate);
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
                                _sys.GiveCurrency(user, currency, (int) refundL);
                        }

                        break;
                    }
                }

                return spawnedTotal;
            }

            for (var i = 0; i < amount; i++)
                if (_sys.TrySpawnProduct(productEntity, user))
                    spawnedTotal++;
                else
                    _sys.GiveCurrency(user, currency, unitPrice);
            return spawnedTotal;
        }
    }
}
