using Content.Shared.Stacks;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private string? TryExecuteBarterCostPlan(EntityUid root, BarterCostPlan plan)
    {
        if (plan.Reservations.Count == 0)
            return "barter cost plan is empty";

        return _transactionCoordinator.TryCommitInventoryTake(
            "BarterCost",
            () =>
            {
                for (var i = 0; i < plan.Reservations.Count; i++)
                {
                    var reservation = plan.Reservations[i];
                    if (!ValidateBarterCostReservation(root, reservation))
                        return $"barter cost reservation #{i} is no longer valid";

                    if (!_inventory.TryTakeReservedEntityUnitsFromRoot(root, reservation.Entity, reservation.Count))
                        return $"failed to consume barter cost reservation #{i}";
                }

                return null;
            });
    }

    private string? TryExecuteBarterCostPlanPreCommit(EntityUid root, BarterCostPlan plan) =>
        TryExecuteBarterCostPlan(root, plan);

    private bool ValidateBarterCostReservation(EntityUid root, BarterCostReservation reservation)
    {
        if (reservation.Entity == EntityUid.Invalid || reservation.Count <= 0)
            return false;

        if (!_ents.EntityExists(reservation.Entity))
            return false;

        if (_inventory.IsProtectedFromDirectSale(root, reservation.Entity))
            return false;

        if (reservation.IsStack)
        {
            if (!_ents.TryGetComponent(reservation.Entity, out StackComponent? stack))
                return false;

            return stack.Count >= reservation.Count;
        }

        return reservation.Count == 1;
    }
}
