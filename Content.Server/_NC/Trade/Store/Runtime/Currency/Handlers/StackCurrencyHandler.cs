using Content.Shared.Stacks;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

/// <summary>
/// Stack-based currency implementation.
/// Currency id is interpreted as <see cref="StackComponent.StackTypeId"/> / <see cref="StackPrototype"/> id.
/// </summary>
public sealed class StackCurrencyHandler : ICurrencyHandler
{
    private readonly IEntityManager _ents;
    private readonly SharedHandsSystem _hands;
    private readonly NcStoreInventorySystem _inventory;
    private readonly IPrototypeManager _protos;
    private readonly SharedStackSystem _stacks;
    private readonly SharedTransformSystem _xform;
    private readonly List<(EntityUid Ent, int Count)> _scratchCandidates = new();
    private readonly List<EntityUid> _scratchItems = new();
    private readonly List<EntityUid> _takePendingDeletesScratch = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _takeStackRestoreScratch = new();
    private readonly List<EntityUid> _issueSpawnedScratch = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _issueStackRestoreScratch = new();
    private readonly List<EntityUid> _transactionIssueSpawnedScratch = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _transactionIssueStackRestoreScratch = new();
    private bool _currencyIssueTransactionActive;

    public StackCurrencyHandler(
        IEntityManager ents,
        SharedHandsSystem hands,
        NcStoreInventorySystem inventory,
        IPrototypeManager protos,
        SharedStackSystem stacks,
        SharedTransformSystem xform)
    {
        _ents = ents;
        _hands = hands;
        _inventory = inventory;
        _protos = protos;
        _stacks = stacks;
        _xform = xform;
    }

    public bool CanHandle(string currencyId)
    {
        if (string.IsNullOrWhiteSpace(currencyId))
            return false;

        // StackType ids are stack prototype ids. Payout also needs a valid spawn prototype,
        // otherwise sell/claim validation could accept currency that cannot actually be issued.
        return _protos.TryIndex<StackPrototype>(currencyId, out var proto) &&
               !string.IsNullOrWhiteSpace(proto.Spawn) &&
               _protos.HasIndex<EntityPrototype>(proto.Spawn);
    }

    public bool TryGetBalance(in NcInventorySnapshot snapshot, string currencyId, out int balance)
    {
        if (string.IsNullOrWhiteSpace(currencyId))
        {
            balance = 0;
            return false;
        }

        balance = snapshot.StackTypeCounts.TryGetValue(currencyId, out var b) ? b : 0;
        return true;
    }

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
                {
                    continue;
                }

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
                if (_ents.EntityExists(ent))
                    _ents.DeleteEntity(ent);
            }

            ClearTakeJournal();
        }
        catch (Exception e)
        {
            Logger.GetSawmill("ncstore-logic")
                .Error($"[NcStore] Failed to take currency '{currencyId}' x{amount}: {e}");
            RollbackTakeJournal(user);
            return false;
        }

        _inventory.InvalidateInventoryCache(user);
        return true;
    }

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
            _hands.TryPickupAnyHand(user, ent, false);
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
            if (_ents.EntityExists(ent))
                _ents.DeleteEntity(ent);
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
            if (_ents.EntityExists(ent))
                _ents.DeleteEntity(ent);
        }

        _inventory.InvalidateInventoryCache(user);
        ClearIssueJournal();
    }

    private void ClearIssueJournal()
    {
        _issueSpawnedScratch.Clear();
        _issueStackRestoreScratch.Clear();
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

    public bool CanGiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;

        if (!CanHandle(currencyId))
            return false;

        return _ents.EntityExists(user) &&
               _ents.TryGetComponent(user, out TransformComponent? _);
    }
}
