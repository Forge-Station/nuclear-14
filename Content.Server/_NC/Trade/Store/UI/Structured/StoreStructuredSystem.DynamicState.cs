using System.Diagnostics;
using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class StoreStructuredSystem : EntitySystem
{
    private const double SlowBarterAvailabilityMs = 5d;
    private const double SlowCratePreviewMs = 5d;
    private const double SlowDynamicStateMs = 10d;

    private readonly StoreDynamicStatePublisher _dynamicStatePublisher = new();

    public void UpdateDynamicState(EntityUid uid, NcStoreComponent comp, EntityUid user)
    {
        if (!_ui.IsUiOpen(uid, StoreUiKey.Key, user))
            return;

        var dynamicStarted = Stopwatch.GetTimestamp();
        if (!_storesUpdatingDynamic.Add(uid))
        {
            Logger.GetSawmill("ncstore-structured")
                .Warning(
                    $"[StoreStructured] Re-entrant UpdateDynamicState on {ToPrettyString(uid)} skipped.");
            return;
        }

        try
        {
            var scratch = GetDynamicScratch(uid);
            var crateUid = GetDynamicCrate(user);
            UpdateStoreWatch(uid, user, crateUid);
            var tabs = GetDynamicTabState(comp);
            var contractNeeds = GetDynamicContractNeeds(comp, tabs.HasContractsTab);
            var scanNeeds = GetDynamicScanNeeds(comp, crateUid, tabs.HasSellTab, contractNeeds);
            var userSnap = ScanDynamicUserInventory(user, scanNeeds, scratch);
            ScanDynamicCrateInventory(crateUid, scanNeeds, scratch);
            UpdateDynamicContractProgress(uid, comp, user, crateUid, tabs, contractNeeds, scratch);

            var buf = scratch.GetWriteBuffer();
            buf.Clear();

            PopulateDynamicBalances(comp, userSnap, buf);
            PopulateDynamicListings(comp, user, userSnap, scratch, buf);
            PopulateDynamicCratePreview(uid, comp, crateUid, tabs.HasSellTab, scanNeeds.NeedCrateScan, scratch, buf);
            PopulateDynamicContracts(uid, comp, tabs.HasContractsTab, scratch, buf);
            PopulateDynamicContractSkip(uid, comp, tabs.HasContractsTab, buf);
            PushDynamicState(uid, comp, tabs, scratch, buf);

            var elapsed = GetElapsedMilliseconds(dynamicStarted);
            if (elapsed > SlowDynamicStateMs)
            {
                Sawmill.Info(
                    $"[StoreStructured] UpdateDynamicState took {elapsed:F2} ms for {ToPrettyString(uid)} " +
                    $"(listings={comp.Listings.Count}, contracts={comp.Contracts.Count}).");
            }
        }
        finally
        {
            _storesUpdatingDynamic.Remove(uid);
        }
    }

    private static double GetElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000d /
        Stopwatch.Frequency;

    private static int CompareContractsForUi(ContractClientData left, ContractClientData right)
    {
        var poolOrder = left.OfferPoolOrder.CompareTo(right.OfferPoolOrder);
        if (poolOrder != 0)
            return poolOrder;

        var name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        if (name != 0)
            return name;

        return string.CompareOrdinal(left.Id, right.Id);
    }

    private EntityUid? GetDynamicCrate(EntityUid user) =>
        _logic.TryGetPulledClosedCrate(user, out var pulledCrate)
            ? pulledCrate
            : null;

    private DynamicTabState GetDynamicTabState(NcStoreComponent comp)
    {
        var hasBuyTab = false;
        var hasSellTab = false;
        var hasBarterTab = false;

        foreach (var listing in comp.Listings)
        {
            if (listing.Mode == StoreMode.Buy)
                hasBuyTab = true;
            else if (listing.Mode == StoreMode.Sell)
                hasSellTab = true;
            else if (listing.Mode == StoreMode.Barter)
                hasBarterTab = true;

            if (hasBuyTab && hasSellTab && hasBarterTab)
                break;
        }

        return new(hasBuyTab, hasSellTab, hasBarterTab, HasContractsProfile(comp));
    }

    private DynamicContractNeeds GetDynamicContractNeeds(NcStoreComponent comp, bool hasContractsTab)
    {
        if (!hasContractsTab)
            return default;

        _contracts.AnalyzeContractProgressRequirements(
            comp,
            out var hasTakenContracts,
            out var needUserItems,
            out var needCrateItems,
            out var needStoreWorldItems);

        return new(hasTakenContracts, needUserItems, needCrateItems, needStoreWorldItems);
    }

    private static DynamicScanNeeds GetDynamicScanNeeds(
        NcStoreComponent comp,
        EntityUid? crateUid,
        bool hasSellTab,
        DynamicContractNeeds contractNeeds
    )
    {
        var needUserSnapshot = NeedsDynamicUserSnapshot(comp);
        var needUserItems = needUserSnapshot || contractNeeds.NeedUserItems;
        var needCrateScan = crateUid != null && (hasSellTab || contractNeeds.NeedCrateItems);
        return new(needUserSnapshot, needUserItems, needCrateScan);
    }

    private static bool NeedsDynamicUserSnapshot(NcStoreComponent comp)
    {
        if (comp.CurrencyWhitelist.Count > 0)
            return true;

        foreach (var listing in comp.Listings)
        {
            if (!string.IsNullOrWhiteSpace(listing.ProductEntity))
                return true;

            if (listing.Mode == StoreMode.Barter && listing.BarterCost.Count > 0)
                return true;
        }

        return false;
    }

    private NcInventorySnapshot? ScanDynamicUserInventory(
        EntityUid user,
        DynamicScanNeeds scanNeeds,
        DynamicScratch scratch
    )
    {
        if (scanNeeds.NeedUserSnapshot)
        {
            _inventory.ScanInventory(user, scratch.DeepUserItems, scratch.UserSnapshot);
            return scratch.UserSnapshot;
        }

        if (scanNeeds.NeedUserItems)
        {
            _inventory.ScanInventoryItems(user, scratch.DeepUserItems);
            scratch.UserSnapshot.Clear();
            return null;
        }

        scratch.DeepUserItems.Clear();
        scratch.UserSnapshot.Clear();
        return null;
    }

    private void ScanDynamicCrateInventory(
        EntityUid? crateUid,
        DynamicScanNeeds scanNeeds,
        DynamicScratch scratch
    )
    {
        if (scanNeeds.NeedCrateScan && crateUid is { } crateEntity)
        {
            _inventory.ScanInventoryItems(crateEntity, scratch.DeepCrateItems);
            // Keep progress preview consistent with claim planning: the pulled closed crate
            // itself may be the turn-in target.
            scratch.DeepCrateItems.Add(crateEntity);
            return;
        }

        scratch.DeepCrateItems.Clear();
    }

    private void UpdateDynamicContractProgress(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid user,
        EntityUid? crateUid,
        DynamicTabState tabs,
        DynamicContractNeeds contractNeeds,
        DynamicScratch scratch
    )
    {
        if (!tabs.HasContractsTab || !contractNeeds.HasTakenContracts)
            return;

        _contracts.UpdateContractsProgress(
            store,
            comp,
            user,
            scratch.DeepUserItems,
            crateUid,
            crateUid != null ? scratch.DeepCrateItems : null,
            contractNeeds.NeedStoreWorldItems);
    }

    private static void PopulateDynamicBalances(
        NcStoreComponent comp,
        NcInventorySnapshot? userSnap,
        DynamicStateBuffer buf
    )
    {
        if (userSnap == null)
            return;

        foreach (var currency in comp.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(currency))
                continue;

            buf.BalancesByCurrency[currency] = userSnap.StackTypeCounts.TryGetValue(currency, out var balance)
                ? balance
                : 0;
        }
    }

    private void PopulateDynamicListings(
        NcStoreComponent comp,
        EntityUid user,
        NcInventorySnapshot? userSnap,
        DynamicScratch scratch,
        DynamicStateBuffer buf
    )
    {
        var barterContextPrepared = false;
        var barterListings = 0;
        long barterTicks = 0;

        foreach (var listing in comp.Listings)
        {
            if (string.IsNullOrWhiteSpace(listing.Id))
                continue;

            var isVisibleBuyListing = IsVisibleBuyListing(listing, scratch);
            if (listing.Mode == StoreMode.Buy && !isVisibleBuyListing)
                continue;

            buf.ListingScopeIds.Add(listing.Id);

            if (ShouldSendListingRemaining(listing, isVisibleBuyListing))
                buf.RemainingById[listing.Id] = listing.RemainingCount;

            if (userSnap == null)
                continue;

            if (listing.Mode != StoreMode.Barter && string.IsNullOrWhiteSpace(listing.ProductEntity))
                continue;

            int owned;
            if (listing.Mode == StoreMode.Barter)
            {
                var barterStarted = Stopwatch.GetTimestamp();
                if (!barterContextPrepared)
                {
                    _logic.PrepareBarterAvailabilityContext(user, scratch.DeepUserItems, scratch.BarterAvailability);
                    barterContextPrepared = true;
                }

                owned = _logic.GetMaxBarterCount(user, listing, userSnap, scratch.BarterAvailability);
                barterTicks += Stopwatch.GetTimestamp() - barterStarted;
                barterListings++;
            }
            else
                owned = _inventory.GetOwnedFromSnapshot(userSnap, listing.ProductEntity, listing.MatchMode);

            if (ShouldSendListingOwned(owned, isVisibleBuyListing) || listing.Mode == StoreMode.Barter)
                buf.OwnedById[listing.Id] = owned;
        }

        if (barterListings > 0)
        {
            var barterMs = barterTicks * 1000d / Stopwatch.Frequency;
            if (barterMs > SlowBarterAvailabilityMs)
            {
                Sawmill.Info(
                    $"[StoreStructured] Barter availability took {barterMs:F2} ms " +
                    $"for {barterListings} listings in profile '{comp.Profile}'.");
            }
        }
    }

    private static bool IsVisibleBuyListing(NcStoreListingDef listing, DynamicScratch scratch) =>
        listing.Mode == StoreMode.Buy && scratch.ShouldSendBuyDynamicFor(listing.Id);

    private static bool ShouldSendListingRemaining(NcStoreListingDef listing, bool isVisibleBuyListing) =>
        listing.RemainingCount != -1 || isVisibleBuyListing;

    private static bool ShouldSendListingOwned(int owned, bool isVisibleBuyListing) => owned > 0 || isVisibleBuyListing;

    private void PopulateDynamicCratePreview(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid? crateUid,
        bool hasSellTab,
        bool needCrateScan,
        DynamicScratch scratch,
        DynamicStateBuffer buf
    )
    {
        if (!hasSellTab || !needCrateScan || crateUid is not { } crate)
        {
            scratch.ResetCachedCratePreview();
            return;
        }

        var inventoryRevision = _logic.GetInventoryRevision(crate);
        if (scratch.TryPopulateCachedCratePreview(crate, comp.CatalogRevision, inventoryRevision, buf))
            return;

        var started = Stopwatch.GetTimestamp();
        var plan = _logic.ComputeMassSellPlanFromCachedItems(store, comp, crate, scratch.DeepCrateItems);
        var elapsed = GetElapsedMilliseconds(started);
        if (elapsed > SlowCratePreviewMs)
        {
            Sawmill.Info(
                $"[StoreStructured] Crate preview took {elapsed:F2} ms for {ToPrettyString(crate)} " +
                $"(items={scratch.DeepCrateItems.Count}, listings={comp.Listings.Count}).");
        }

        scratch.CacheCratePreview(crate, comp.CatalogRevision, inventoryRevision, plan);
        scratch.TryPopulateCachedCratePreview(crate, comp.CatalogRevision, inventoryRevision, buf);
    }

    private readonly record struct DynamicTabState(
        bool HasBuyTab,
        bool HasSellTab,
        bool HasBarterTab,
        bool HasContractsTab);

    private readonly record struct DynamicContractNeeds(
        bool HasTakenContracts,
        bool NeedUserItems,
        bool NeedCrateItems,
        bool NeedStoreWorldItems);

    private readonly record struct DynamicScanNeeds(
        bool NeedUserSnapshot,
        bool NeedUserItems,
        bool NeedCrateScan);
}
