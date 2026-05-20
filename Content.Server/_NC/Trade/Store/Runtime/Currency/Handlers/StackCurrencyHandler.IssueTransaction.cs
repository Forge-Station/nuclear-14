using Content.Shared.Stacks;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class StackCurrencyHandler : ICurrencyHandler
{
    public bool BeginCurrencyIssueTransaction()
    {
        if (_currencyIssueTransactionActive)
            return false;

        ClearIssueJournal();
        _transactionIssueSpawnedScratch.Clear();
        _transactionIssueStackRestoreScratch.Clear();
        _currencyIssueTransactionActive = true;
        return true;
    }

    public void CommitCurrencyIssueTransaction(EntityUid user)
    {
        if (!_currencyIssueTransactionActive)
            return;

        for (var i = 0; i < _transactionIssueSpawnedScratch.Count; i++)
        {
            var ent = _transactionIssueSpawnedScratch[i];
            if (!_ents.EntityExists(ent))
                continue;

            // Placement is best-effort at commit time. If hands are still full, the stack remains
            // at the receiver's coordinates instead of failing an already validated payout.
            TryPickupIssuedCurrencyBestEffort(user, ent);
        }

        _inventory.InvalidateInventoryCache(user);
        _currencyIssueTransactionActive = false;
        _transactionIssueSpawnedScratch.Clear();
        _transactionIssueStackRestoreScratch.Clear();
        ClearIssueJournal();
    }

    public void RollbackCurrencyIssueTransaction(EntityUid user)
    {
        if (!_currencyIssueTransactionActive)
            return;

        RollbackIssueJournal(user);

        for (var i = _transactionIssueStackRestoreScratch.Count - 1; i >= 0; i--)
        {
            var (ent, previousCount) = _transactionIssueStackRestoreScratch[i];
            if (_ents.TryGetComponent(ent, out StackComponent? stack))
                _stacks.SetCount(ent, previousCount, stack);
        }

        for (var i = 0; i < _transactionIssueSpawnedScratch.Count; i++)
        {
            var ent = _transactionIssueSpawnedScratch[i];
            DeleteFinalEntityBestEffort(ent, "CurrencyIssueRollback");
        }

        _inventory.InvalidateInventoryCache(user);
        _currencyIssueTransactionActive = false;
        _transactionIssueSpawnedScratch.Clear();
        _transactionIssueStackRestoreScratch.Clear();
        ClearIssueJournal();
    }

    private void HandleSuccessfulIssueJournal(EntityUid user)
    {
        if (_currencyIssueTransactionActive)
        {
            MergeIssueJournalIntoTransaction();
            ClearIssueJournal();
            _inventory.InvalidateInventoryCache(user);
            return;
        }

        ClearIssueJournal();
    }

    private void MergeIssueJournalIntoTransaction()
    {
        for (var i = 0; i < _issueStackRestoreScratch.Count; i++)
        {
            var restore = _issueStackRestoreScratch[i];
            var alreadyTracked = false;

            for (var j = 0; j < _transactionIssueStackRestoreScratch.Count; j++)
            {
                if (_transactionIssueStackRestoreScratch[j].Ent != restore.Ent)
                    continue;

                alreadyTracked = true;
                break;
            }

            if (!alreadyTracked)
                _transactionIssueStackRestoreScratch.Add(restore);
        }

        _transactionIssueSpawnedScratch.AddRange(_issueSpawnedScratch);
    }

    private void TrackIssueStackRestore(EntityUid ent, int previousCount)
    {
        for (var i = 0; i < _issueStackRestoreScratch.Count; i++)
        {
            if (_issueStackRestoreScratch[i].Ent == ent)
                return;
        }

        _issueStackRestoreScratch.Add((ent, previousCount));
    }

    private void RollbackIssueJournal(EntityUid user)
    {
        for (var i = _issueStackRestoreScratch.Count - 1; i >= 0; i--)
        {
            var (ent, previousCount) = _issueStackRestoreScratch[i];
            if (_ents.TryGetComponent(ent, out StackComponent? stack))
                _stacks.SetCount(ent, previousCount, stack);
        }

        for (var i = 0; i < _issueSpawnedScratch.Count; i++)
        {
            var ent = _issueSpawnedScratch[i];
            DeleteFinalEntityBestEffort(ent, "CurrencyIssueRollback");
        }

        _inventory.InvalidateInventoryCache(user);
        ClearIssueJournal();
    }

    private void ClearIssueJournal()
    {
        _issueSpawnedScratch.Clear();
        _issueStackRestoreScratch.Clear();
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
            Sawmill.Error($"[NcStore] {context}: failed to delete final currency entity {ent}: {e}");
        }
    }

    private void TryPickupIssuedCurrencyBestEffort(EntityUid user, EntityUid ent)
    {
        try
        {
            _hands.TryPickupAnyHand(user, ent, false);
        }
        catch (Exception e)
        {
            Sawmill.Warning($"[NcStore] Failed to pick up issued currency stack {ent} for {user}: {e}");
        }
    }

    private void TrackTakeStackRestore(EntityUid ent, int previousCount)
    {
        for (var i = 0; i < _takeStackRestoreScratch.Count; i++)
        {
            if (_takeStackRestoreScratch[i].Ent == ent)
                return;
        }

        _takeStackRestoreScratch.Add((ent, previousCount));
    }

    private void RollbackTakeJournal(EntityUid user)
    {
        for (var i = _takeStackRestoreScratch.Count - 1; i >= 0; i--)
        {
            var (ent, previousCount) = _takeStackRestoreScratch[i];
            if (_ents.TryGetComponent(ent, out StackComponent? stack))
                _stacks.SetCount(ent, previousCount, stack);
        }

        _inventory.InvalidateInventoryCache(user);
        ClearTakeJournal();
    }

    private void ClearTakeJournal()
    {
        _takePendingDeletesScratch.Clear();
        _takeStackRestoreScratch.Clear();
    }
}
