using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private sealed class StoreTransactionCoordinator : IStoreTransactionCoordinator
    {
        private readonly NcStoreLogicSystem _sys;

        public StoreTransactionCoordinator(NcStoreLogicSystem sys)
        {
            _sys = sys;
        }

        public List<ContractRewardData> BuildSingleReward(StoreRewardType type, string id, int amount)
        {
            return new List<ContractRewardData>(1)
            {
                new(type, id, amount)
            };
        }

        public List<ContractRewardData> BuildCurrencyRewards(IReadOnlyDictionary<string, int> incomeByCurrency)
        {
            var rewards = new List<ContractRewardData>(incomeByCurrency.Count);
            foreach (var (currency, amount) in incomeByCurrency)
            {
                if (amount <= 0 || string.IsNullOrWhiteSpace(currency))
                    continue;

                rewards.Add(new ContractRewardData(StoreRewardType.Currency, currency, amount));
            }

            return rewards;
        }

        public string? TryCommitInventoryTake(string context, Func<string?> takeAction)
        {
            if (!_sys._inventory.BeginTakeTransaction())
                return $"{context}: inventory take transaction is already active.";

            try
            {
                var failure = takeAction();
                if (!string.IsNullOrWhiteSpace(failure))
                {
                    _sys._inventory.RollbackTakeTransaction();
                    return failure;
                }

                _sys._inventory.CommitTakeTransaction();
                return null;
            }
            catch (Exception e)
            {
                _sys._inventory.RollbackTakeTransaction();
                return $"{context}: inventory take transaction threw {e.GetType().Name}: {e.Message}";
            }
        }
    }
}
