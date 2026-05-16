using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private bool TryRefundBarterCostPlan(EntityUid root, BarterCostPlan plan)
    {
        if (plan.Reservations.Count == 0)
            return false;

        var refundedAll = true;

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            var reservation = plan.Reservations[i];
            if (reservation.Count <= 0)
                continue;

            if (reservation.IsStack)
            {
                if (string.IsNullOrWhiteSpace(reservation.StackType) ||
                    !_protos.HasIndex<StackPrototype>(reservation.StackType))
                {
                    Sawmill.Warning(
                        $"[NcStore] Failed to refund barter stack cost: missing stack type '{reservation.StackType}' " +
                        $"for entity {reservation.Entity} x{reservation.Count}.");
                    refundedAll = false;
                    continue;
                }

                GiveCurrency(root, reservation.StackType, reservation.Count);
                continue;
            }

            if (string.IsNullOrWhiteSpace(reservation.Prototype) ||
                !_protos.HasIndex<EntityPrototype>(reservation.Prototype))
            {
                Sawmill.Warning(
                    $"[NcStore] Failed to refund barter item cost: missing entity prototype '{reservation.Prototype}' " +
                    $"for entity {reservation.Entity} x{reservation.Count}.");
                refundedAll = false;
                continue;
            }

            var spawned = TrySpawnProductUnits(reservation.Prototype, root, reservation.Count);
            if (spawned >= reservation.Count)
                continue;

            Sawmill.Warning(
                $"[NcStore] Incomplete barter item cost refund for prototype '{reservation.Prototype}': " +
                $"refunded {spawned}/{reservation.Count}.");
            refundedAll = false;
        }

        _inventory.InvalidateInventoryCache(root);
        return refundedAll;
    }
}
