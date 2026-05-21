using Content.Shared.Stacks;


namespace Content.Server._NC.Trade;


public sealed partial class StackCurrencyHandler : ICurrencyHandler
{
    public bool TryTake(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;
        if (!CanHandle(currencyId))
            return false;

        _inventory.ScanInventoryItems(user, _scratchItems);

        _scratchCandidates.Clear();
        var total = 0;

        foreach (var ent in _scratchItems)
        {
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;
            if (_inventory.IsProtectedFromDirectSale(user, ent))
                continue;

            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != currencyId)
                continue;

            var cnt = Math.Max(st.Count, 0);
            if (cnt <= 0)
                continue;

            _scratchCandidates.Add((ent, cnt));
            total += cnt;
        }

        if (total < amount)
            return false;

        _scratchCandidates.Sort((a, b) => a.Count.CompareTo(b.Count));

        _takePendingDeletesScratch.Clear();
        _takeStackRestoreScratch.Clear();

        try
        {
            var left = amount;
            foreach (var (ent, _) in _scratchCandidates)
            {
                if (left <= 0)
                    break;

                if (!_ents.EntityExists(ent) ||
                    _inventory.IsProtectedFromDirectSale(user, ent) ||
                    !_ents.TryGetComponent(ent, out StackComponent? st) ||
                    st.StackTypeId != currencyId)
                    continue;

                var have = Math.Max(st.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, left);
                TrackTakeStackRestore(ent, st.Count);
                _stacks.SetCount(ent, have - take, st);
                left -= take;

                if (st.Count <= 0)
                    _takePendingDeletesScratch.Add(ent);
            }

            if (left > 0)
            {
                RollbackTakeJournal(user);
                return false;
            }

            for (var i = 0; i < _takePendingDeletesScratch.Count; i++)
            {
                var ent = _takePendingDeletesScratch[i];
                DeleteFinalEntityBestEffort(ent, "CurrencyTake");
            }

            ClearTakeJournal();
        }
        catch (Exception e)
        {
            Sawmill.Error($"[NcStore] Failed to take currency '{currencyId}' x{amount}: {e}");
            RollbackTakeJournal(user);
            return false;
        }

        _inventory.InvalidateInventoryCache(user);
        return true;
    }
}
