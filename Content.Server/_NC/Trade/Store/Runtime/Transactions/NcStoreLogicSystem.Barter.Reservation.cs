using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private bool TryExecuteBarterCostPlan(EntityUid root, BarterCostPlan plan)
    {
        if (plan.Reservations.Count == 0)
            return false;

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            if (!ValidateBarterCostReservation(root, plan.Reservations[i]))
                return false;
        }

        for (var i = 0; i < plan.Reservations.Count; i++)
        {
            var reservation = plan.Reservations[i];
            if (reservation.IsStack)
            {
                if (!_ents.TryGetComponent(reservation.Entity, out StackComponent? stack))
                    return false;

                var newCount = stack.Count - reservation.Count;
                _stacks.SetCount(reservation.Entity, Math.Max(0, newCount), stack);
                if (stack.Count <= 0)
                    _ents.DeleteEntity(reservation.Entity);

                continue;
            }

            _ents.DeleteEntity(reservation.Entity);
        }

        _inventory.InvalidateInventoryCache(root);
        return true;
    }

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
