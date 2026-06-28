using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private IStoreRewardSpawner _spawnService = default!;
    private IStoreTransactionCoordinator _transactionCoordinator = default!;

    private IStoreCurrencyService Currency => _currency;

    public bool TryTakeCurrency(EntityUid user, string stackType, int amount) =>
        Currency.TryTakeCurrency(user, stackType, amount);

    private void InitializeServices()
    {
        _spawnService = new StoreSpawnService(this);
        _transactionCoordinator = new StoreTransactionCoordinator(this);
    }

    public bool TryPickCurrencyForBuy(
        NcStoreComponent store,
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    ) =>
        Currency.TryPickCurrencyForBuy(store, listing, snapshot, out currency, out unitPrice, out balance);

    public bool TryPickCurrencyForSell(
        NcStoreComponent store,
        NcStoreListingDef listing,
        out string currency,
        out int unitPrice
    ) =>
        Currency.TryPickCurrencyForSell(store, listing, out currency, out unitPrice);

    public bool CanHandleCurrency(string stackType) => Currency.CanHandleCurrency(stackType);

    public bool CanGiveCurrency(EntityUid user, string stackType, int amount) =>
        Currency.CanGiveCurrency(user, stackType, amount);

    public bool TryGiveCurrency(EntityUid user, string stackType, int amount) =>
        Currency.TryGiveCurrency(user, stackType, amount);

    private bool BeginCurrencyIssueTransaction() => Currency.BeginCurrencyIssueTransaction();

    private void CommitCurrencyIssueTransaction(EntityUid user) => Currency.CommitCurrencyIssueTransaction(user);

    private void RollbackCurrencyIssueTransaction(EntityUid user) => Currency.RollbackCurrencyIssueTransaction(user);


    private sealed partial class StoreSpawnService : IStoreRewardSpawner
    {
        private readonly List<EntityUid> _scratchItems = new();
        private readonly List<EntityUid> _spawnedScratch = new();
        private readonly string _stackComponentName;
        private readonly List<(EntityUid Ent, int PreviousCount)> _stackRestoreScratch = new();
        private readonly NcStoreLogicSystem _sys;
        private readonly List<EntityUid> _transactionSpawnedScratch = new();
        private readonly List<(EntityUid Ent, int PreviousCount)> _transactionStackRestoreScratch = new();
        private bool _rewardTransactionActive;

        public StoreSpawnService(NcStoreLogicSystem sys)
        {
            _sys = sys;
            _stackComponentName = _sys._compFactory.GetComponentName(typeof(StackComponent));
        }

        public int SpawnRewardProduct(
            EntityUid user,
            string productEntity,
            EntityPrototype productProto,
            int units
        ) =>
            SpawnPurchasedProduct(user, productEntity, productProto, 1, units);

        public bool BeginRewardTransaction()
        {
            if (_rewardTransactionActive)
                return false;

            ResetPurchaseBatchState();
            _transactionSpawnedScratch.Clear();
            _transactionStackRestoreScratch.Clear();
            _rewardTransactionActive = true;
            return true;
        }

        public void CommitRewardTransaction(EntityUid user)
        {
            if (!_rewardTransactionActive)
                return;

            for (var i = 0; i < _transactionSpawnedScratch.Count; i++)
                _sys.QueuePickupToHandsOrCrateNextTick(user, _transactionSpawnedScratch[i]);

            _sys._inventory.InvalidateInventoryCache(user);
            _rewardTransactionActive = false;
            _transactionSpawnedScratch.Clear();
            _transactionStackRestoreScratch.Clear();
            ResetPurchaseBatchState();
        }

        public void RollbackRewardTransaction()
        {
            if (!_rewardTransactionActive)
                return;

            RollbackPurchaseBatch();

            for (var i = 0; i < _transactionStackRestoreScratch.Count; i++)
            {
                var (ent, previousCount) = _transactionStackRestoreScratch[i];
                if (!_sys._ents.TryGetComponent(ent, out StackComponent? stack))
                    continue;

                _sys._stacks.SetCount(ent, previousCount, stack);
            }

            for (var i = 0; i < _transactionSpawnedScratch.Count; i++)
            {
                var ent = _transactionSpawnedScratch[i];
                DeleteSpawnedBestEffort(ent, "RewardRollback");
            }

            _rewardTransactionActive = false;
            _transactionSpawnedScratch.Clear();
            _transactionStackRestoreScratch.Clear();
            ResetPurchaseBatchState();
        }

        public int SpawnPurchasedProduct(
            EntityUid user,
            string productEntity,
            EntityPrototype productProto,
            int purchases,
            int unitsPerPurchase
        )
        {
            if (purchases <= 0 || unitsPerPurchase <= 0)
                return 0;

            if (TryGetStackPurchaseConfig(productProto, out var stackTypeId, out var maxPerStack))
                return SpawnStackPurchasedProduct(
                    user,
                    productEntity,
                    purchases,
                    unitsPerPurchase,
                    stackTypeId,
                    maxPerStack);

            return SpawnSinglePurchasedProduct(user, productEntity, purchases, unitsPerPurchase);
        }
    }
}
