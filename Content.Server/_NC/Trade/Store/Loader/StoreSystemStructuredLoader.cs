using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed class StoreSystemStructuredLoader : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-loader");

    [Dependency] private readonly NcContractSystem _contracts = default!;

    private readonly HashSet<EntityUid> _contractsInitialized = new();
    private readonly HashSet<EntityUid> _loadedStores = new();
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NcStoreComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NcStoreComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NcStoreComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnTerminating(EntityUid uid, NcStoreComponent comp, ref EntityTerminatingEvent args)
    {
        _loadedStores.Remove(uid);
        _contractsInitialized.Remove(uid);
        _contracts.ClearStoreRuntimeCaches(uid);
    }

    private void OnMapInit(EntityUid uid, NcStoreComponent comp, MapInitEvent args) =>
        EnsureLoadedInternal(uid, comp, "MapInit", true);

    public void EnsureLoaded(EntityUid uid, NcStoreComponent comp, string reason) =>
        EnsureLoadedInternal(uid, comp, reason, true);

    private void OnStartup(EntityUid uid, NcStoreComponent comp, ComponentStartup args) =>
        EnsureLoadedInternal(uid, comp, "Startup", true);

    private void EnsureLoadedInternal(EntityUid uid, NcStoreComponent comp, string reason, bool allowContractsInit)
    {
        var changed = false;

        if (_loadedStores.Add(uid))
        {
            TryLoadProfile(uid, comp, reason);
            comp.RebuildListingIndex();
            changed = true;
        }

        if (changed)
            comp.BumpCatalogRevision();

        if (allowContractsInit && !_contractsInitialized.Contains(uid))
        {
            _contracts.RefillContractsForStore(uid, comp);
            _contractsInitialized.Add(uid);
        }
    }

    private void TryLoadProfile(EntityUid uid, NcStoreComponent comp, string reason)
    {
        comp.CurrencyWhitelist.Clear();
        comp.Categories.Clear();
        comp.Listings.Clear();
        comp.ListingIndex.Clear();

        if (!_prototypes.TryIndex<NcStoreProfilePrototype>(comp.Profile, out var profile))
        {
            Sawmill.Warning($"[NcStore] {ToPrettyString(uid)}: profile '{comp.Profile}' not found (reason={reason}).");
            return;
        }

        var ctx = new LoadContext();
        var total = 0;

        foreach (var id in profile.Buy)
            total += LoadPresetForMode(id, StoreMode.Buy, comp, ctx);

        foreach (var id in profile.Sell)
            total += LoadPresetForMode(id, StoreMode.Sell, comp, ctx);

        if (total == 0 && profile.Contracts == null)
        {
            Sawmill.Warning(
                $"[NcStore] {ToPrettyString(uid)}: profile '{profile.ID}' has no buy, sell or contracts (reason={reason}).");
            return;
        }

        WarnIfContractSkipCurrencyMissing(uid, comp, profile, reason);

        Sawmill.Info(
            $"[NcStore] {ToPrettyString(uid)}: profile='{profile.ID}', loaded {total} listings, " +
            $"buy={profile.Buy.Count}, sell={profile.Sell.Count}, " +
            $"contracts={(profile.Contracts != null ? profile.Contracts.Value.ToString() : "<none>")}, reason={reason}");
    }

    private void WarnIfContractSkipCurrencyMissing(
        EntityUid uid,
        NcStoreComponent comp,
        NcStoreProfilePrototype profile,
        string reason)
    {
        if (profile.Buy.Count > 0 || profile.Sell.Count > 0)
            return;

        if (profile.Contracts is not { } contractsId)
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(contractsId, out var contractsPreset))
            return;

        if (contractsPreset.SkipCost <= 0 || !string.IsNullOrWhiteSpace(contractsPreset.SkipCurrency))
            return;

        if (comp.CurrencyWhitelist.Count > 0)
            return;

        Sawmill.Warning(
            $"[NcStore] {ToPrettyString(uid)}: contract-only profile '{profile.ID}' uses contract preset " +
            $"'{contractsPreset.ID}' with skipCost={contractsPreset.SkipCost} but no skipCurrency " +
            $"(reason={reason}). Contract skip will be disabled.");
    }

    private int LoadPresetForMode(
        ProtoId<StorePresetStructuredPrototype> presetId,
        StoreMode mode,
        NcStoreComponent comp,
        LoadContext ctx)
    {
        if (!_prototypes.TryIndex<StorePresetStructuredPrototype>(presetId, out var preset))
        {
            Sawmill.Warning($"[NcStore] Preset '{presetId}' not found.");
            return 0;
        }

        var count = 0;

        if (!string.IsNullOrWhiteSpace(preset.Currency) && ctx.CurrencySeen.Add(preset.Currency))
            comp.CurrencyWhitelist.Add(preset.Currency);

        foreach (var categoryId in preset.Categories)
        {
            if (!_prototypes.TryIndex<StoreCategoryStructuredPrototype>(categoryId, out var categoryProto))
            {
                Sawmill.Error($"[NcStore] Category '{categoryId}' not found (preset='{presetId}').");
                continue;
            }

            var categoryName = categoryProto.Name;

            if (ctx.CategorySeen.Add(categoryName))
                comp.Categories.Add(categoryName);

            foreach (var entry in categoryProto.Entries)
            {
                if (entry.MatchMode == PrototypeMatchMode.Matcher &&
                    !ValidateMatcherEntry(entry, mode, presetId, categoryId))
                {
                    continue;
                }

                var baseId = $"{presetId}:{mode}:{categoryId}:{entry.Proto}";
                var id = AllocateDeterministicId(baseId, ctx);

                var listing = new NcStoreListingDef
                {
                    Id = id,
                    ProductEntity = entry.Proto,
                    MatchMode = entry.MatchMode,
                    Mode = mode,
                    Categories = new List<string> { categoryName },
                    Conditions = new List<ListingConditionPrototype>(),
                    RemainingCount = entry.Count ?? -1,
                    UnitsPerPurchase = Math.Max(1, entry.Amount),
                    Cost = new()
                };

                if (!string.IsNullOrWhiteSpace(preset.Currency))
                    listing.Cost[preset.Currency] = entry.Price;

                comp.Listings.Add(listing);
                count++;
            }
        }

        return count;
    }

    private bool ValidateMatcherEntry(
        StoreCatalogEntry entry,
        StoreMode mode,
        ProtoId<StorePresetStructuredPrototype> presetId,
        string categoryId)
    {
        if (string.IsNullOrWhiteSpace(entry.Proto))
        {
            Sawmill.Warning(
                $"[NcStore] Matcher entry in '{presetId}/{categoryId}' has empty proto and was skipped.");
            return false;
        }

        if (!_prototypes.TryIndex<NcMatcherPrototype>(entry.Proto, out var matcher))
        {
            Sawmill.Warning(
                $"[NcStore] Matcher '{entry.Proto}' not found (preset='{presetId}', category='{categoryId}') and was skipped.");
            return false;
        }

        var hasItems = matcher.Items is { Count: > 0 };
        var hasTags = matcher.Tags is { Count: > 0 };
        if (!hasItems && !hasTags)
        {
            Sawmill.Warning(
                $"[NcStore] Matcher '{entry.Proto}' has neither items nor tags and was skipped.");
            return false;
        }

        // Buy listing must be able to spawn, which means items are required (tags do not drive spawn).
        if (mode == StoreMode.Buy && !hasItems)
        {
            Sawmill.Warning(
                $"[NcStore] Matcher '{entry.Proto}' is used in a buy listing without items (tags-only), " +
                $"cannot spawn and was skipped (preset='{presetId}', category='{categoryId}').");
            return false;
        }

        return true;
    }

    private static string AllocateDeterministicId(string baseId, LoadContext ctx)
    {
        if (!ctx.NextSuffixByBaseId.TryGetValue(baseId, out var nextSuffix))
        {
            if (ctx.ListingIds.Add(baseId))
            {
                ctx.NextSuffixByBaseId[baseId] = 1;
                return baseId;
            }

            nextSuffix = 1;
        }

        while (true)
        {
            var candidate = $"{baseId}#{nextSuffix}";
            if (ctx.ListingIds.Add(candidate))
            {
                ctx.NextSuffixByBaseId[baseId] = nextSuffix + 1;
                return candidate;
            }

            nextSuffix++;
        }
    }

    private sealed class LoadContext
    {
        public readonly HashSet<string> CategorySeen = new(StringComparer.Ordinal);
        public readonly HashSet<string> CurrencySeen = new(StringComparer.Ordinal);
        public readonly HashSet<string> ListingIds = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> NextSuffixByBaseId = new(StringComparer.Ordinal);
    }
}
