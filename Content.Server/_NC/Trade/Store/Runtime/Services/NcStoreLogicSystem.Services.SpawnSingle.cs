using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private sealed partial class StoreSpawnService
    {
        private void DeleteSpawnedBestEffort(EntityUid ent, string context)
        {
            if (ent == EntityUid.Invalid || !_sys.Exists(ent))
                return;

            try
            {
                _sys._ents.DeleteEntity(ent);
            }
            catch (Exception e)
            {
                Sawmill.Error($"[NcStore] {context}: failed to delete spawned reward entity {_sys.ToPrettyString(ent)}: {e}");
            }
        }

        private int SpawnSinglePurchasedProduct(EntityUid user, string productEntity, int purchases, int unitsPerPurchase)
        {
            var successfulPurchases = 0;

            for (var i = 0; i < purchases; i++)
            {
                if (!TrySpawnSinglePurchaseBatch(user, productEntity, unitsPerPurchase))
                    break;

                successfulPurchases++;
            }

            return successfulPurchases * unitsPerPurchase;
        }

        private bool TrySpawnSinglePurchaseBatch(EntityUid user, string productEntity, int unitsPerPurchase)
        {
            ResetPurchaseBatchState();
            if (!TryGetUserSpawnCoordinates(user, out var userCoords))
                return false;

            for (var i = 0; i < unitsPerPurchase; i++)
            {
                if (!TrySpawnPurchaseEntity(productEntity, userCoords, out var spawned))
                {
                    RollbackPurchaseBatch();
                    return false;
                }

                _spawnedScratch.Add(spawned);
            }

            CommitPurchaseBatch(user);
            return true;
        }
    }
}
