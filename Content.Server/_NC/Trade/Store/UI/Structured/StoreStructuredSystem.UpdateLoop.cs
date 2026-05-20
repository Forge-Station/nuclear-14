using System.Linq;
using Content.Server.Popups;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Access.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ProcessPendingRefreshes();
        ProcessDirtyStoreUpdates();

        var now = _timing.CurTime;

        if (now >= _nextRealtimeOpenStoreUpdate)
        {
            _nextRealtimeOpenStoreUpdate = now + RealtimeOpenStoreUpdateInterval;
            ProcessRealtimeOpenStoreUpdates();
        }

        if (now >= _nextOpenStoreValidityCheck)
        {
            _nextOpenStoreValidityCheck = now + OpenStoreValidityCheckInterval;
            ProcessOpenStoreValidityChecks();
        }
    }

    private void ProcessRealtimeOpenStoreUpdates()
    {
        if (_openStoreUids.Count == 0)
        {
            _realtimeOpenStoreCursor = 0;
            return;
        }

        var now = _timing.CurTime;
        _openStoresScratch.Clear();
        _openStoresScratch.AddRange(_openStoreUids);

        if (_realtimeOpenStoreCursor >= _openStoresScratch.Count)
            _realtimeOpenStoreCursor = 0;

        var processed = 0;
        var inspected = 0;
        var count = _openStoresScratch.Count;

        while (inspected < count && processed < MaxRealtimeDynamicUpdatesPerTick)
        {
            var index = (_realtimeOpenStoreCursor + inspected) % count;
            if (ProcessRealtimeOpenStoreUpdate(_openStoresScratch[index], now))
                processed++;

            inspected++;
        }

        _realtimeOpenStoreCursor = (_realtimeOpenStoreCursor + Math.Max(1, inspected)) % count;
    }

    private bool ProcessRealtimeOpenStoreUpdate(EntityUid uid, TimeSpan now)
    {
        if (!TryGetOpenStoreUser(uid, out var store, out var user))
            return false;

        if (EnsureCrateWatchUpToDate(uid, user))
            MarkDirty(uid);

        if (!_contracts.HasRealtimeContractState(store) || !TryGetDynamicScratchForUpdate(uid, now, out var scratch))
            return false;

        _dirtyStores.Remove(uid);
        UpdateDynamicState(uid, store, user);
        SetNextDynamicUpdateTime(scratch, now);
        return true;
    }

    private void ProcessDirtyStoreUpdates()
    {
        if (_dirtyStores.Count == 0)
            return;

        var now = _timing.CurTime;
        var processed = 0;

        _dirtyStoresScratch.Clear();
        _dirtyStoresScratch.AddRange(_dirtyStores);

        foreach (var uid in _dirtyStoresScratch)
        {
            if (processed >= MaxDynamicUpdatesPerTick)
                break;

            if (!TryGetOpenStoreUser(uid, out var store, out var user))
            {
                _dirtyStores.Remove(uid);
                continue;
            }

            if (!TryGetDynamicScratchForUpdate(uid, now, out var scratch))
                continue;

            UpdateDynamicState(uid, store, user);
            SetNextDynamicUpdateTime(scratch, now);
            _dirtyStores.Remove(uid);
            processed++;
        }
    }

    private void ProcessOpenStoreValidityChecks()
    {
        if (_openStoreUids.Count == 0)
            return;

        _openStoresScratch.Clear();
        _openStoresScratch.AddRange(_openStoreUids);

        foreach (var uid in _openStoresScratch)
            ValidateOpenStore(uid);
    }

    private bool TryGetOpenStoreUser(EntityUid uid, out NcStoreComponent store, out EntityUid user)
    {
        store = default!;
        user = default;

        if (!TryComp(uid, out NcStoreComponent? foundStore) || foundStore.CurrentUser is not { } currentUser)
            return false;

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, currentUser))
            return false;

        store = foundStore;
        user = currentUser;
        return true;
    }

    private bool TryGetDynamicScratchForUpdate(EntityUid uid, TimeSpan now, out DynamicScratch scratch)
    {
        scratch = GetDynamicScratch(uid);
        return now >= scratch.NextDynamicAllowed;
    }

    private void SetNextDynamicUpdateTime(DynamicScratch scratch, TimeSpan now)
    {
        scratch.NextDynamicAllowed = now + TimeSpan.FromSeconds(MinDynamicInterval);
    }

    private void ValidateOpenStore(EntityUid uid)
    {
        if (!TryComp(uid, out NcStoreComponent? store) || !TryComp(uid, out TransformComponent? xform))
        {
            CloseAndCleanUp(uid);
            return;
        }

        if (store.CurrentUser is not { } userUid)
        {
            CloseAndCleanUp(uid);
            return;
        }

        if (!IsStoreUserInRange(xform, userUid))
        {
            CloseStoreForDetachedUser(uid, store, userUid);
            return;
        }

        if (_storeSystem.CanUseStore(uid, store, userUid))
            return;

        CloseStoreForNoAccess(uid, store, userUid);
    }

    private bool IsStoreUserInRange(TransformComponent storeXform, EntityUid userUid)
    {
        return TryComp(userUid, out TransformComponent? userXform) &&
               _xform.InRange(storeXform.Coordinates, userXform.Coordinates, AutoCloseDistance);
    }

    private void CloseStoreForDetachedUser(EntityUid uid, NcStoreComponent store, EntityUid userUid)
    {
        CloseAndCleanUp(uid, userUid);
        store.CurrentUser = null;
    }

    private void CloseStoreForNoAccess(EntityUid uid, NcStoreComponent store, EntityUid userUid)
    {
        CloseAndCleanUp(uid, userUid);
        store.CurrentUser = null;
        _popups.PopupEntity(Loc.GetString("nc-store-no-access"), uid, userUid);
    }

    private void CloseAndCleanUp(EntityUid storeUid, EntityUid? user = null)
    {
        if (_watchByStore.TryGetValue(storeUid, out var info))
        {
            if (info.User != EntityUid.Invalid)
                _inventory.InvalidateInventoryCache(info.User);

            if (info.Crate is { } crate)
                _inventory.InvalidateInventoryCache(crate);
        }

        if (user != null)
            _ui.CloseUi(storeUid, StoreUiKey.Key, user.Value);

        if (_dynamicScratchByStore.TryGetValue(storeUid, out var scratch))
            scratch.UpdateVisibleIds(null);
        _openStoreUids.Remove(storeUid);
        UnregisterStoreWatch(storeUid);
        _dirtyStores.Remove(storeUid);
        _storesUpdatingDynamic.Remove(storeUid);
        _dynamicScratchByStore.Remove(storeUid);
    }

    private void MarkDirty(EntityUid storeUid)
    {
        if (storeUid != EntityUid.Invalid)
            _dirtyStores.Add(storeUid);
    }
}
