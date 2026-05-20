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
    private bool TryGetLockedUiUser(EntityUid store, NcStoreComponent comp, out EntityUid user)
    {
        user = default;
        if (comp.CurrentUser is not { } cur || cur == EntityUid.Invalid)
            return false;

        if (!_ui.IsUiOpen(store, StoreUiKey.Key, cur))
            return false;

        user = cur;
        return true;
    }

    private void OnSetVisibleListings(EntityUid uid, NcStoreComponent comp, StoreSetVisibleListingsBoundUiMessage msg)
    {
        if (!TryGetLockedUiUser(uid, comp, out var user))
            return;

        _visibleListingIdsScratch.Clear();
        _visibleListingIdsSetScratch.Clear();

        var ids = msg.Ids;
        var max = Math.Min(ids.Length, MaxVisibleListingIds);

        for (var i = 0; i < max; i++)
        {
            var id = ids[i];
            if (!StoreTradeLimits.IsValidMessageId(id))
                continue;

            if (!comp.ListingIndex.ContainsKey(NcStoreComponent.MakeListingKey(StoreMode.Buy, id)))
                continue;

            if (!_visibleListingIdsSetScratch.Add(id))
                continue;

            _visibleListingIdsScratch.Add(id);
        }

        var scratch = GetDynamicScratch(uid);
        if (!scratch.UpdateVisibleIds(_visibleListingIdsScratch.Count > 0 ? _visibleListingIdsScratch : null))
            return;

        RequestDynamicRefresh(uid, comp, user);
    }

    private void OnStorageOpen(EntityUid uid, EntityStorageComponent comp, ref StorageAfterOpenEvent args)
    {
        if (_storesByWatchedRoot.ContainsKey(uid))
            RefreshStoresAffectedBy(uid);
    }

    private void OnContractsChanged(EntityUid uid, NcStoreComponent comp, ref NcContractsChangedEvent args)
    {
        MarkDirty(uid);
    }

    private void OnStorageClose(EntityUid uid, EntityStorageComponent comp, ref StorageAfterCloseEvent args)
    {
        if (_storesByWatchedRoot.ContainsKey(uid))
            RefreshStoresAffectedBy(uid);
    }

    private void OnStoreShutdown(EntityUid uid, NcStoreComponent comp, ComponentShutdown args)
    {
        _catalogCache.Clear(uid);
        _dynamicScratchByStore.Remove(uid);
        _contracts.ClearStoreRuntimeCaches(uid);
        _logic.ClearStoreRuntimeCaches(uid);

        if (_openStoreUids.Contains(uid) || _watchByStore.ContainsKey(uid) || _dirtyStores.Contains(uid))
        {
            EntityUid? user = null;

            if (_watchByStore.TryGetValue(uid, out var watch) && watch.User != EntityUid.Invalid)
                user = watch.User;
            else if (comp.CurrentUser is { } cur && cur != EntityUid.Invalid)
                user = cur;

            CloseAndCleanUp(uid, user);
        }
    }

    public void RefreshCatalog(EntityUid uid, NcStoreComponent comp)
    {
        _catalogCache.Clear(uid);
        _dynamicScratchByStore.Remove(uid);

        comp.BumpCatalogRevision();

        if (comp.CurrentUser is not { } user)
            return;

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            return;

        SendCatalog(uid, comp, user);
        RequestDynamicRefresh(uid, comp, user);
    }

    public void RequestDynamicRefresh(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        MarkDirty(uid);

        var now = _timing.CurTime;
        var scratch = GetDynamicScratch(uid);
        if (now < scratch.NextDynamicAllowed)
            return;

        _dirtyStores.Remove(uid);
        UpdateDynamicState(uid, comp, user);
        SetNextDynamicUpdateTime(scratch, now);
    }

    private void OnUiOpenAttempt(EntityUid uid, NcStoreComponent comp, ref ActivatableUIOpenAttemptEvent ev)
    {
        ev.Cancel();
        var user = ev.User;

        if (!_ui.HasUi(uid, StoreUiKey.Key))
            return;
        if (!_storeSystem.CanUseStore(uid, comp, user))
            return;
        if (comp.CurrentUser is { } current && current != user)
            return;
        if (TryComp(uid, out TransformComponent? sX) && TryComp(user, out TransformComponent? uX) &&
            !_xform.InRange(sX.Coordinates, uX.Coordinates, AutoCloseDistance))
            return;

        var wasInUse = comp.CurrentUser != null;
        comp.CurrentUser = user;
        if (!wasInUse)
            _openStoreUids.Add(uid);

        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            _ui.OpenUi(uid, StoreUiKey.Key, user);

        EnsureCrateWatchUpToDate(uid, user);

        _loader.EnsureLoaded(uid, comp, "UiOpenAttempt");

        SendCatalog(uid, comp, user);
        RequestDynamicRefresh(uid, comp, user);
    }

    private void OnUiClosed(EntityUid uid, NcStoreComponent comp, BoundUIClosedEvent ev)
    {
        if (!ev.UiKey.Equals(StoreUiKey.Key))
            return;
        comp.CurrentUser = null;
        CloseAndCleanUp(uid);
    }

    private void OnUiRefreshRequest(EntityUid uid, NcStoreComponent comp, RequestUiRefreshMessage msg)
    {
        if (!TryGetLockedUiUser(uid, comp, out var user))
        {
            CloseAndCleanUp(uid);
            return;
        }

        if (!_storeSystem.CanUseStore(uid, comp, user))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            CloseAndCleanUp(uid);
            return;
        }

        if (TryComp(uid, out TransformComponent? sX) && TryComp(user, out TransformComponent? uX) &&
            !_xform.InRange(sX.Coordinates, uX.Coordinates, AutoCloseDistance))
        {
            _ui.CloseUi(uid, StoreUiKey.Key, user);
            comp.CurrentUser = null;
            CloseAndCleanUp(uid);
            return;
        }

        EnsureCrateWatchUpToDate(uid, user);

        var scratch = GetDynamicScratch(uid);
        var now = _timing.CurTime;
        if (now < scratch.NextManualRefreshAllowed)
        {
            MarkDirty(uid);
            return;
        }

        scratch.NextManualRefreshAllowed = now + TimeSpan.FromSeconds(MinManualRefreshInterval);
        SendCatalog(uid, comp, user);
        RequestDynamicRefresh(uid, comp, user);
    }

    private void OnAccessReaderChanged(
        EntityUid uid,
        AccessReaderComponent comp,
        ref AccessReaderConfigurationChangedEvent args
    )
    {
        if (TryComp<NcStoreComponent>(uid, out var store) && store.CurrentUser is { } user)
        {
            if (!_storeSystem.CanUseStore(uid, store, user))
            {
                _ui.CloseUi(uid, StoreUiKey.Key, user);
                store.CurrentUser = null;
                CloseAndCleanUp(uid);
            }
        }
    }
}
