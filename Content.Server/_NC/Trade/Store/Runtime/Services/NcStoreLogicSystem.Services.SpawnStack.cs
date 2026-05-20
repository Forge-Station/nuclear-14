using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private sealed partial class StoreSpawnService
    {
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
            int purchases,
            int unitsPerPurchase,
            string? stackTypeId,
            int maxPerStack)
        {
            var successfulPurchases = 0;

            for (var i = 0; i < purchases; i++)
            {
                if (!TrySpawnStackPurchaseBatch(user, productEntity, unitsPerPurchase, stackTypeId, maxPerStack))
                    break;

                successfulPurchases++;
            }

            return FinalizeSuccessfulStackPurchases(user, successfulPurchases, unitsPerPurchase);
        }

        private int FinalizeSuccessfulStackPurchases(EntityUid user, int successfulPurchases, int unitsPerPurchase)
        {
            if (successfulPurchases <= 0)
                return 0;

            _sys._inventory.InvalidateInventoryCache(user);
            return successfulPurchases * unitsPerPurchase;
        }

        private bool TrySpawnStackPurchaseBatch(
            EntityUid user,
            string productEntity,
            int unitsPerPurchase,
            string? stackTypeId,
            int maxPerStack)
        {
            PrepareStackPurchaseBatch(user);

            var remainingToSpawn = unitsPerPurchase;
            FillExistingPurchasedStacks(_scratchItems, stackTypeId, maxPerStack, ref remainingToSpawn);

            if (!TryCompleteStackPurchaseBatch(user, productEntity, remainingToSpawn, maxPerStack))
            {
                HandleFailedPurchaseBatch(user);
                return false;
            }

            CommitPurchaseBatch(user);
            return true;
        }

        private void PrepareStackPurchaseBatch(EntityUid user)
        {
            _sys._inventory.ScanInventoryItems(user, _scratchItems);
            ResetPurchaseBatchState();
        }

        private bool TryCompleteStackPurchaseBatch(
            EntityUid user,
            string productEntity,
            int remainingToSpawn,
            int maxPerStack)
        {
            return remainingToSpawn <= 0 ||
                   TrySpawnRemainingPurchasedStacks(user, productEntity, remainingToSpawn, maxPerStack);
        }

        private void HandleFailedPurchaseBatch(EntityUid user)
        {
            RollbackPurchaseBatch();
            _sys._inventory.InvalidateInventoryCache(user);
        }

        private void FillExistingPurchasedStacks(
            List<EntityUid> cachedItems,
            string? stackTypeId,
            int maxPerStack,
            ref int remainingToSpawn)
        {
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

                TrackStackRestore(ent, existingStack.Count);
                var toAdd = Math.Min(spaceLeft, remainingToSpawn);
                _sys._stacks.SetCount(ent, existingStack.Count + toAdd, existingStack);

                remainingToSpawn -= toAdd;
            }
        }

        private void TrackStackRestore(EntityUid ent, int previousCount)
        {
            for (var i = 0; i < _stackRestoreScratch.Count; i++)
            {
                if (_stackRestoreScratch[i].Ent == ent)
                    return;
            }

            _stackRestoreScratch.Add((ent, previousCount));
        }

        private bool TrySpawnRemainingPurchasedStacks(
            EntityUid user,
            string productEntity,
            int remainingToSpawn,
            int maxPerStack)
        {
            if (!TryGetUserSpawnCoordinates(user, out var userCoords))
                return false;

            while (remainingToSpawn > 0)
            {
                var chunk = Math.Min(remainingToSpawn, maxPerStack);
                if (!TrySpawnPurchasedStackChunk(productEntity, userCoords, chunk))
                    return false;

                remainingToSpawn -= chunk;
            }

            return true;
        }

        private bool TryGetUserSpawnCoordinates(EntityUid user, out EntityCoordinates userCoords)
        {
            userCoords = default;
            if (!_sys._ents.TryGetComponent(user, out TransformComponent? userXform))
                return false;

            userCoords = userXform.Coordinates;
            return true;
        }

        private bool TrySpawnPurchasedStackChunk(
            string productEntity,
            EntityCoordinates userCoords,
            int chunk)
        {
            if (!TrySpawnPurchaseEntity(productEntity, userCoords, out var spawned))
                return false;

            if (_sys._ents.TryGetComponent(spawned, out StackComponent? spawnedStack))
                _sys._stacks.SetCount(spawned, chunk, spawnedStack);

            _spawnedScratch.Add(spawned);
            return true;
        }

        private bool TrySpawnPurchaseEntity(string productEntity, EntityCoordinates userCoords, out EntityUid spawned)
        {
            spawned = default;

            try
            {
                spawned = _sys._ents.SpawnEntity(productEntity, userCoords);
                return true;
            }
            catch (Exception e)
            {
                Sawmill.Error($"[NcStore] Spawn failed during purchase batch: {e}");
                return false;
            }
        }

        private void CommitPurchaseBatch(EntityUid user)
        {
            if (_rewardTransactionActive)
            {
                MergeBatchIntoRewardTransaction();
                ResetPurchaseBatchState();
                return;
            }

            for (var i = 0; i < _spawnedScratch.Count; i++)
                _sys.QueuePickupToHandsOrCrateNextTick(user, _spawnedScratch[i]);

            ResetPurchaseBatchState();
        }

        private void MergeBatchIntoRewardTransaction()
        {
            for (var i = 0; i < _stackRestoreScratch.Count; i++)
            {
                var restore = _stackRestoreScratch[i];
                var alreadyTracked = false;
                for (var j = 0; j < _transactionStackRestoreScratch.Count; j++)
                {
                    if (_transactionStackRestoreScratch[j].Ent != restore.Ent)
                        continue;

                    alreadyTracked = true;
                    break;
                }

                if (!alreadyTracked)
                    _transactionStackRestoreScratch.Add(restore);
            }

            _transactionSpawnedScratch.AddRange(_spawnedScratch);
        }

        private void ResetPurchaseBatchState()
        {
            _spawnedScratch.Clear();
            _stackRestoreScratch.Clear();
        }

        private void RollbackPurchaseBatch()
        {
            for (var i = 0; i < _stackRestoreScratch.Count; i++)
            {
                var (ent, previousCount) = _stackRestoreScratch[i];
                if (!_sys._ents.TryGetComponent(ent, out StackComponent? stack))
                    continue;

                _sys._stacks.SetCount(ent, previousCount, stack);
            }

            for (var i = 0; i < _spawnedScratch.Count; i++)
            {
                var ent = _spawnedScratch[i];
                DeleteSpawnedBestEffort(ent, "RewardBatchRollback");
            }

            ResetPurchaseBatchState();
        }
    }
}
