using Content.Shared.Stacks;


namespace Content.Server._NC.Trade;


public sealed partial class StackCurrencyHandler : ICurrencyHandler
{
    public bool TryGiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
            return true; // Nothing to give, operation is trivially successful.
        if (!CanGiveCurrency(user, currencyId, amount) ||
            !_protos.TryIndex<StackPrototype>(currencyId, out var proto))
            return false;

        _inventory.InvalidateInventoryCache(user);
        _issueSpawnedScratch.Clear();
        _issueStackRestoreScratch.Clear();

        try
        {
            var maxPerStack = proto.MaxCount ?? int.MaxValue;
            if (maxPerStack <= 0)
                maxPerStack = 1;

            long remaining = amount;

            _inventory.ScanInventoryItems(user, _scratchItems);
            foreach (var ent in _scratchItems)
            {
                if (remaining <= 0)
                    break;
                if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != currencyId)
                    continue;

                var canAdd = (long) maxPerStack - st.Count;
                if (canAdd <= 0)
                    continue;

                var add = Math.Min(canAdd, remaining);
                var newCount = (int) Math.Clamp(st.Count + add, 0L, maxPerStack);

                TrackIssueStackRestore(ent, st.Count);
                _stacks.SetCount(ent, newCount, st);
                remaining -= add;
            }

            if (remaining <= 0)
            {
                _inventory.InvalidateInventoryCache(user);
                HandleSuccessfulIssueJournal(user);
                return true;
            }

            var coords = _xform.GetMoverCoordinates(user);

            while (remaining > 0)
            {
                var addL = Math.Min(remaining, maxPerStack);
                var add = (int) Math.Clamp(addL, 1L, maxPerStack);

                EntityUid spawned;
                try
                {
                    spawned = _ents.SpawnEntity(proto.Spawn, coords);
                }
                catch (Exception e)
                {
                    Logger.GetSawmill("ncstore-logic")
                        .Error($"[NcStore] Failed to spawn currency stack '{currencyId}' using '{proto.Spawn}': {e}");
                    RollbackIssueJournal(user);
                    return false;
                }

                _issueSpawnedScratch.Add(spawned);

                if (_ents.TryGetComponent(spawned, out StackComponent? newStack))
                    _stacks.SetCount(spawned, add, newStack);

                if (!_currencyIssueTransactionActive && !_hands.TryPickupAnyHand(user, spawned, false))
                {
                    Logger.GetSawmill("ncstore-logic")
                        .Warning($"[NcStore] Failed to place issued currency stack '{currencyId}' on {user}.");
                    RollbackIssueJournal(user);
                    return false;
                }

                remaining -= add;
            }

            _inventory.InvalidateInventoryCache(user);
            HandleSuccessfulIssueJournal(user);
            return true;
        }
        catch (Exception e)
        {
            Logger.GetSawmill("ncstore-logic")
                .Error($"[NcStore] Failed to issue currency '{currencyId}' x{amount}: {e}");
            RollbackIssueJournal(user);
            return false;
        }
    }
}
