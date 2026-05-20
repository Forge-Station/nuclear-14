using Content.Shared._NC.Trade;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreInventorySystem
{
    public bool BeginTakeTransaction()
    {
        if (_takeTransactionActive)
            return false;

        _takeTransactionStackRestoreScratch.Clear();
        _takeTransactionDeleteScratch.Clear();
        _takeTransactionActive = true;
        return true;
    }

    public void CommitTakeTransaction()
    {
        if (!_takeTransactionActive)
            return;

        for (var i = 0; i < _takeTransactionDeleteScratch.Count; i++)
        {
            var ent = _takeTransactionDeleteScratch[i];
            DeleteFinalEntityBestEffort(ent, "InventoryTake");
        }

        InvalidateAllCaches();
        ResetTakeTransaction();
    }

    private void DeleteFinalEntityBestEffort(EntityUid ent, string context)
    {
        if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
            return;

        try
        {
            _ents.DeleteEntity(ent);
        }
        catch (Exception e)
        {
            Sawmill.Error($"[{context}] Failed to delete final entity {ent}: {e}");
        }
    }

    public void RollbackTakeTransaction()
    {
        if (!_takeTransactionActive)
            return;

        for (var i = _takeTransactionStackRestoreScratch.Count - 1; i >= 0; i--)
        {
            var (ent, previousCount) = _takeTransactionStackRestoreScratch[i];
            if (_ents.TryGetComponent(ent, out StackComponent? stack))
                _stacks.SetCount(ent, previousCount, stack);
        }

        InvalidateAllCaches();
        ResetTakeTransaction();
    }

    private void ResetTakeTransaction()
    {
        _takeTransactionActive = false;
        _takeTransactionStackRestoreScratch.Clear();
        _takeTransactionDeleteScratch.Clear();
    }

    private void TrackTakeTransactionStackRestore(EntityUid ent, int previousCount)
    {
        if (!_takeTransactionActive)
            return;

        for (var i = 0; i < _takeTransactionStackRestoreScratch.Count; i++)
        {
            if (_takeTransactionStackRestoreScratch[i].Ent == ent)
                return;
        }

        _takeTransactionStackRestoreScratch.Add((ent, previousCount));
    }
}
