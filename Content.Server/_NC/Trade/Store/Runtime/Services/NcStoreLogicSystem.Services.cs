using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private StoreSpawnService _spawnService = default!;

    private void InitializeServices() => _spawnService = new(this);

    public bool TryPickCurrencyForBuy(
        NcStoreComponent store,
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    ) =>
        _currency.TryPickCurrencyForBuy(store, listing, snapshot, out currency, out unitPrice, out balance);

    public bool TryPickCurrencyForSell(
        NcStoreComponent store,
        NcStoreListingDef listing,
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
        private readonly string _stackComponentName;
        private readonly NcStoreLogicSystem _sys;
        public StoreSpawnService(NcStoreLogicSystem sys)
        {
            _sys = sys;
            _stackComponentName = _sys._compFactory.GetComponentName(typeof(StackComponent));
        }

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

            if (TryGetStackPurchaseConfig(productProto, out var stackTypeId, out var maxPerStack))
                return SpawnStackPurchasedProduct(user, productEntity, amount, stackTypeId, maxPerStack);

            return SpawnSinglePurchasedProduct(user, productEntity, amount);
        }

        private bool TryGetStackPurchaseConfig(
            EntityPrototype productProto,
            out string? stackTypeId,
            out int maxPerStack)
        {
            stackTypeId = null;
            maxPerStack = 0;

            if (!productProto.TryGetComponent(_stackComponentName, out StackComponent? stackComp))
                return false;

            stackTypeId = stackComp.StackTypeId;
            maxPerStack = ResolvePurchaseMaxStack(stackTypeId);
            return true;
        }

        private int ResolvePurchaseMaxStack(string? stackTypeId)
        {
            if (!string.IsNullOrWhiteSpace(stackTypeId) &&
                _sys._protos.TryIndex<StackPrototype>(stackTypeId, out var stackTypeProto))
                return Math.Max(1, stackTypeProto.MaxCount ?? int.MaxValue);

            return int.MaxValue;
        }

        private int SpawnStackPurchasedProduct(
            EntityUid user,
            string productEntity,
            int amount,
            string? stackTypeId,
            int maxPerStack)
        {
            var cachedItems = _sys._inventory.GetOrBuildDeepItemsCacheCompacted(user);
            var remainingToSpawn = amount;
            var spawnedTotal = FillExistingPurchasedStacks(cachedItems, stackTypeId, maxPerStack, ref remainingToSpawn);

            if (remainingToSpawn > 0)
                spawnedTotal += SpawnRemainingPurchasedStacks(user, productEntity, remainingToSpawn, maxPerStack);

            _sys._inventory.InvalidateInventoryCache(user);
            return spawnedTotal;
        }

        private int FillExistingPurchasedStacks(
            List<EntityUid> cachedItems,
            string? stackTypeId,
            int maxPerStack,
            ref int remainingToSpawn)
        {
            var spawnedTotal = 0;

            foreach (var ent in cachedItems)
            {
                if (remainingToSpawn <= 0)
                    break;

                if (!_sys._ents.TryGetComponent(ent, out StackComponent? existingStack) ||
                    existingStack.StackTypeId != stackTypeId)
                    continue;

                var spaceLeft = maxPerStack - existingStack.Count;
                if (spaceLeft <= 0)
                    continue;

                var toAdd = Math.Min(spaceLeft, remainingToSpawn);
                _sys._stacks.SetCount(ent, existingStack.Count + toAdd, existingStack);

                remainingToSpawn -= toAdd;
                spawnedTotal += toAdd;
            }

            return spawnedTotal;
        }

        private int SpawnRemainingPurchasedStacks(
            EntityUid user,
            string productEntity,
            int remainingToSpawn,
            int maxPerStack)
        {
            var spawnedTotal = 0;
            var userCoords = _sys._ents.GetComponent<TransformComponent>(user).Coordinates;

            while (remainingToSpawn > 0)
            {
                var chunk = Math.Min(remainingToSpawn, maxPerStack);
                if (!TrySpawnPurchasedStackChunk(user, productEntity, userCoords, chunk))
                    break;

                spawnedTotal += chunk;
                remainingToSpawn -= chunk;
            }

            return spawnedTotal;
        }

        private bool TrySpawnPurchasedStackChunk(
            EntityUid user,
            string productEntity,
            EntityCoordinates userCoords,
            int chunk)
        {
            try
            {
                var spawned = _sys._ents.SpawnEntity(productEntity, userCoords);
                if (_sys._ents.TryGetComponent(spawned, out StackComponent? spawnedStack))
                    _sys._stacks.SetCount(spawned, chunk, spawnedStack);

                _sys.QueuePickupToHandsOrCrateNextTick(user, spawned);
                return true;
            }
            catch (Exception e)
            {
                Logger.GetSawmill("ncstore-logic").Error($"Spawn failed during bulk buy: {e}");
                return false;
            }
        }

        private int SpawnSinglePurchasedProduct(EntityUid user, string productEntity, int amount)
        {
            var spawnedTotal = 0;

            for (var i = 0; i < amount; i++)
            {
                if (_sys.TrySpawnProduct(productEntity, user))
                    spawnedTotal++;
            }

            return spawnedTotal;
        }
    }
}
