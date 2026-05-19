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

        var stackRestore = new List<(EntityUid Ent, int PreviousCount)>(plan.Reservations.Count);
        var pendingDeletes = new List<EntityUid>(plan.Reservations.Count);

        try
        {
            for (var i = 0; i < plan.Reservations.Count; i++)
            {
                var reservation = plan.Reservations[i];
                if (reservation.IsStack)
                {
                    if (!_ents.TryGetComponent(reservation.Entity, out StackComponent? stack))
                        return false;

                    stackRestore.Add((reservation.Entity, stack.Count));
                    var newCount = stack.Count - reservation.Count;
                    _stacks.SetCount(reservation.Entity, Math.Max(0, newCount), stack);
                    if (stack.Count <= 0)
                        pendingDeletes.Add(reservation.Entity);

                    continue;
                }

                pendingDeletes.Add(reservation.Entity);
            }

            for (var i = 0; i < pendingDeletes.Count; i++)
            {
                var ent = pendingDeletes[i];
                if (_ents.EntityExists(ent))
                    _ents.DeleteEntity(ent);
            }
        }
        catch
        {
            for (var i = stackRestore.Count - 1; i >= 0; i--)
            {
                var (ent, previousCount) = stackRestore[i];
                if (_ents.TryGetComponent(ent, out StackComponent? stack))
                    _stacks.SetCount(ent, previousCount, stack);
            }

            throw;
        }

        _inventory.InvalidateInventoryCache(root);
        return true;
    }

    private string? TryExecuteBarterCostPlanPreCommit(EntityUid root, BarterCostPlan plan)
    {
        try
        {
            return TryExecuteBarterCostPlan(root, plan)
                ? null
                : "barter cost could not be consumed";
        }
        catch (Exception e)
        {
            Sawmill.Error($"[NcStore] Barter cost pre-commit failed unexpectedly: {e}");
            return $"barter cost consumption threw {e.GetType().Name}: {e.Message}";
        }
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
