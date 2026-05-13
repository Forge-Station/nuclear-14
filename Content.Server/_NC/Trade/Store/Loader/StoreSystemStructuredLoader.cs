using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
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

        foreach (var id in profile.Barter)
            total += LoadBarterPreset(id, comp, ctx);

        AddContractSkipCurrencyIfNeeded(comp, profile, ctx);

        if (total == 0 && profile.Contracts == null)
        {
            Sawmill.Warning(
                $"[NcStore] {ToPrettyString(uid)}: profile '{profile.ID}' has no buy, sell or contracts (reason={reason}).");
            return;
        }

        WarnIfContractSkipCurrencyMissing(uid, comp, profile, reason);

        Sawmill.Info(
            $"[NcStore] {ToPrettyString(uid)}: profile='{profile.ID}', loaded {total} listings, " +
            $"buy={profile.Buy.Count}, sell={profile.Sell.Count}, barter={profile.Barter.Count}, " +
            $"contracts={(profile.Contracts != null ? profile.Contracts.Value.ToString() : "<none>")}, reason={reason}");
    }

    private void WarnIfContractSkipCurrencyMissing(
        EntityUid uid,
        NcStoreComponent comp,
        NcStoreProfilePrototype profile,
        string reason)
    {
        if (profile.Contracts is not { } contractsId)
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(contractsId, out var contractsPreset))
            return;

        if (contractsPreset.SkipCost <= 0 || !string.IsNullOrWhiteSpace(contractsPreset.SkipCurrency))
            return;

        if (comp.CurrencyWhitelist.Count > 0)
            return;

        Sawmill.Warning(
            $"[NcStore] {ToPrettyString(uid)}: profile '{profile.ID}' uses contract preset " +
            $"'{contractsPreset.ID}' with skipCost={contractsPreset.SkipCost}, but no skipCurrency " +
            $"and no catalog currencies were resolved (reason={reason}). Contract skip will be disabled.");
    }

    private void AddContractSkipCurrencyIfNeeded(
        NcStoreComponent comp,
        NcStoreProfilePrototype profile,
        LoadContext ctx)
    {
        if (profile.Contracts is not { } contractsId)
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(contractsId, out var contractsPreset))
            return;

        if (contractsPreset.SkipCost <= 0)
            return;

        var skipCurrency = contractsPreset.SkipCurrency;
        if (string.IsNullOrWhiteSpace(skipCurrency))
            return;

        if (ctx.CurrencySeen.Add(skipCurrency))
            comp.CurrencyWhitelist.Add(skipCurrency);
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



    private int LoadBarterPreset(
        ProtoId<NcBarterPresetPrototype> presetId,
        NcStoreComponent comp,
        LoadContext ctx)
    {
        if (!_prototypes.TryIndex<NcBarterPresetPrototype>(presetId, out var preset))
        {
            Sawmill.Warning($"[NcStore] Barter preset '{presetId}' not found.");
            return 0;
        }

        var count = 0;

        foreach (var categoryId in preset.Categories)
        {
            if (!_prototypes.TryIndex<NcBarterCategoryPrototype>(categoryId, out var categoryProto))
            {
                Sawmill.Error($"[NcStore] Barter category '{categoryId}' not found (preset='{presetId}').");
                continue;
            }

            var categoryName = categoryProto.Name;
            if (ctx.CategorySeen.Add(categoryName))
                comp.Categories.Add(categoryName);

            if (categoryProto.Listings.Count == 0)
            {
                Sawmill.Warning($"[NcStore] Barter category '{categoryId}' in preset '{presetId}' has no listings.");
                continue;
            }

            for (var i = 0; i < categoryProto.Listings.Count; i++)
            {
                var listingId = categoryProto.Listings[i];
                if (!_prototypes.TryIndex<NcBarterListingPrototype>(listingId, out var listingProto))
                {
                    Sawmill.Warning(
                        $"[NcStore] Barter listing '{listingId}' not found " +
                        $"(preset='{presetId}', category='{categoryId}', listings[{i}]).");
                    continue;
                }

                count += TryAddBarterListing(listingProto, presetId, categoryId, categoryName, comp, ctx);
            }
        }

        return count;
    }

    private int TryAddBarterListing(
        NcBarterListingPrototype listingProto,
        ProtoId<NcBarterPresetPrototype> presetId,
        ProtoId<NcBarterCategoryPrototype> categoryId,
        string categoryName,
        NcStoreComponent comp,
        LoadContext ctx)
    {
        if (!ValidateBarterListing(listingProto, presetId, categoryId))
            return 0;

        AddBarterCurrenciesToWhitelist(comp, ctx, listingProto);

        var baseId = $"{presetId}:Barter:{categoryId}:{listingProto.ID}";
        var id = AllocateDeterministicId(baseId, ctx);
        var icon = ResolveBarterIcon(listingProto);

        if (string.IsNullOrWhiteSpace(icon))
        {
            Sawmill.Warning(
                $"[NcStore] Barter listing '{listingProto.ID}' in '{presetId}/{categoryId}' has no resolvable icon and was skipped.");
            return 0;
        }

        var listing = new NcStoreListingDef
        {
            Id = id,
            ProductEntity = icon,
            DisplayName = listingProto.Name,
            Description = listingProto.Description,
            MatchMode = PrototypeMatchMode.Exact,
            Mode = StoreMode.Barter,
            Categories = new List<string> { categoryName },
            Conditions = new List<ListingConditionPrototype>(),
            RemainingCount = listingProto.Count,
            UnitsPerPurchase = 1,
            BarterCost = CloneBarterCost(listingProto.Cost),
            BarterReceive = CloneBarterReceive(listingProto.Receive),
            BarterReceivePools = CloneBarterReceivePools(listingProto.ReceivePools),
            Cost = new()
        };

        comp.Listings.Add(listing);
        return 1;
    }

    private void AddBarterCurrenciesToWhitelist(NcStoreComponent comp, LoadContext ctx, NcBarterListingPrototype listingProto)
    {
        foreach (var cost in listingProto.Cost)
        {
            if (!string.IsNullOrWhiteSpace(cost.Currency) && ctx.CurrencySeen.Add(cost.Currency))
                comp.CurrencyWhitelist.Add(cost.Currency);
        }

        foreach (var receive in listingProto.Receive)
        {
            if (!string.IsNullOrWhiteSpace(receive.Currency) && ctx.CurrencySeen.Add(receive.Currency))
                comp.CurrencyWhitelist.Add(receive.Currency);
        }

        foreach (var pool in listingProto.ReceivePools)
            AddRewardPoolCurrenciesToWhitelist(comp, ctx, pool.Pool);
    }

    private void AddRewardPoolCurrenciesToWhitelist(NcStoreComponent comp, LoadContext ctx, string poolId)
    {
        if (string.IsNullOrWhiteSpace(poolId) ||
            !_prototypes.TryIndex<NcContractRewardPoolPrototype>(poolId, out var pool))
            return;

        for (var i = 0; i < pool.Entries.Count; i++)
        {
            var reward = pool.Entries[i];
            if (reward.Type != StoreRewardType.Currency)
                continue;

            var currency = GetRewardId(reward);
            if (!string.IsNullOrWhiteSpace(currency) && ctx.CurrencySeen.Add(currency))
                comp.CurrencyWhitelist.Add(currency);
        }
    }

    private bool ValidateBarterListing(
        NcBarterListingPrototype listingProto,
        ProtoId<NcBarterPresetPrototype> presetId,
        ProtoId<NcBarterCategoryPrototype> categoryId)
    {
        if (string.IsNullOrWhiteSpace(listingProto.ID))
        {
            Sawmill.Warning($"[NcStore] Barter entry in '{presetId}/{categoryId}' has empty id and was skipped.");
            return false;
        }

        if (listingProto.Cost.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{listingProto.ID}' has no cost and was skipped.");
            return false;
        }

        if (listingProto.Receive.Count == 0 && listingProto.ReceivePools.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{listingProto.ID}' has no receive or receivePools block and was skipped.");
            return false;
        }

        var ok = true;

        for (var i = 0; i < listingProto.Cost.Count; i++)
            ok &= ValidateBarterCost(listingProto.ID, $"cost[{i}]", listingProto.Cost[i]);

        for (var i = 0; i < listingProto.Receive.Count; i++)
            ok &= ValidateBarterReceive(listingProto.ID, $"receive[{i}]", listingProto.Receive[i]);

        for (var i = 0; i < listingProto.ReceivePools.Count; i++)
            ok &= ValidateBarterReceivePool(listingProto.ID, $"receivePools[{i}]", listingProto.ReceivePools[i]);

        if (listingProto.Count == 0 || listingProto.Count < -1)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{listingProto.ID}' has invalid count={listingProto.Count}. Use -1 or a positive value.");
            ok = false;
        }

        return ok;
    }

    private bool ValidateBarterCost(string entryId, string path, NcBarterCostEntry cost)
    {
        var sources = CountNonEmpty(cost.Prototype, cost.Group, cost.Currency);
        if (sources != 1)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} must specify exactly one of prototype/group/currency.");
            return false;
        }

        if (cost.Count <= 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has non-positive count={cost.Count}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(cost.Prototype) && !_prototypes.HasIndex<EntityPrototype>(cost.Prototype))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing entity prototype '{cost.Prototype}'.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(cost.Group) && !ValidateBarterItemGroup(entryId, path, cost.Group))
            return false;

        if (!string.IsNullOrWhiteSpace(cost.Currency) && !_prototypes.HasIndex<StackPrototype>(cost.Currency))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing stack currency '{cost.Currency}'.");
            return false;
        }

        return true;
    }

    private bool ValidateBarterReceive(string entryId, string path, NcBarterReceiveEntry receive)
    {
        var sources = CountNonEmpty(receive.Prototype, receive.Currency);
        if (sources != 1)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} must specify exactly one of prototype/currency.");
            return false;
        }

        if (receive.Count <= 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has non-positive count={receive.Count}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(receive.Prototype) && !_prototypes.HasIndex<EntityPrototype>(receive.Prototype))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing entity prototype '{receive.Prototype}'.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(receive.Currency) && !_prototypes.HasIndex<StackPrototype>(receive.Currency))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing stack currency '{receive.Currency}'.");
            return false;
        }

        return true;
    }

    private bool ValidateBarterReceivePool(string entryId, string path, NcBarterReceivePoolEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Pool))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has empty pool id.");
            return false;
        }

        if (entry.Rolls.Min <= 0 || entry.Rolls.Max <= 0 || entry.Rolls.Min > entry.Rolls.Max)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} has invalid rolls range " +
                $"{entry.Rolls.Min}..{entry.Rolls.Max}.");
            return false;
        }

        if (entry.Chance < 0f || entry.Chance > 1f)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has invalid chance={entry.Chance}. Expected 0..1.");
            return false;
        }

        if (!_prototypes.TryIndex<NcContractRewardPoolPrototype>(entry.Pool, out var pool) || pool.Entries.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing or empty reward pool '{entry.Pool}'.");
            return false;
        }

        var ok = true;
        for (var i = 0; i < pool.Entries.Count; i++)
            ok &= ValidateBarterReceivePoolReward(entryId, $"{path}.pool[{i}]", pool.Entries[i]);

        if (ok)
            return true;

        Sawmill.Warning(
            $"[NcStore] Barter entry '{entryId}' {path} pool '{entry.Pool}' contains entries that are not valid for barter. " +
            "Only Item and Currency rewards are supported; nested pools are rejected.");
        return false;
    }

    private bool ValidateBarterReceivePoolReward(string entryId, string path, ContractRewardDef reward)
    {
        if (reward.Type != StoreRewardType.Item && reward.Type != StoreRewardType.Currency)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} must be Item or Currency. Nested pools are not supported in Barter V1.1.");
            return false;
        }

        if (reward.Weight <= 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has non-positive weight={reward.Weight}.");
            return false;
        }

        var amountRange = GetRewardAmountRange(reward);
        if (amountRange.Min < 0 || amountRange.Max <= 0 || amountRange.Min > amountRange.Max)
        {
            Sawmill.Warning(
                $"[NcStore] Barter entry '{entryId}' {path} has invalid count/amount range " +
                $"{amountRange.Min}..{amountRange.Max}.");
            return false;
        }

        var chance = reward.Chance >= 0f ? reward.Chance : reward.Probability;
        if (chance < 0f || chance > 1f)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has invalid chance={chance}. Expected 0..1.");
            return false;
        }

        var rewardId = GetRewardId(reward);
        if (string.IsNullOrWhiteSpace(rewardId))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} has empty reward id.");
            return false;
        }

        if (reward.Type == StoreRewardType.Item && !_prototypes.HasIndex<EntityPrototype>(rewardId))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing entity prototype '{rewardId}'.");
            return false;
        }

        if (reward.Type == StoreRewardType.Currency && !_prototypes.HasIndex<StackPrototype>(rewardId))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing stack currency '{rewardId}'.");
            return false;
        }

        return true;
    }

    private bool ValidateBarterItemGroup(string entryId, string path, string groupId)
    {
        if (!_prototypes.TryIndex<NcItemGroupPrototype>(groupId, out var group))
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references missing item group '{groupId}'.");
            return false;
        }

        if (group.Prototypes.Count == 0 && group.Tags.Count == 0)
        {
            Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} references empty item group '{groupId}'.");
            return false;
        }

        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var protoId = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(protoId) || !_prototypes.HasIndex<EntityPrototype>(protoId))
            {
                Sawmill.Warning(
                    $"[NcStore] Barter entry '{entryId}' {path} item group '{groupId}' has invalid prototype '{protoId}'.");
                return false;
            }
        }

        for (var i = 0; i < group.Tags.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(group.Tags[i]))
            {
                Sawmill.Warning($"[NcStore] Barter entry '{entryId}' {path} item group '{groupId}' has empty tag.");
                return false;
            }
        }

        return true;
    }

    private string ResolveBarterIcon(NcBarterListingPrototype listingProto)
    {
        if (!string.IsNullOrWhiteSpace(listingProto.Icon) && _prototypes.HasIndex<EntityPrototype>(listingProto.Icon))
            return listingProto.Icon;

        foreach (var receive in listingProto.Receive)
        {
            if (!string.IsNullOrWhiteSpace(receive.Prototype))
                return receive.Prototype;

            if (!string.IsNullOrWhiteSpace(receive.Currency) && TryResolveCurrencyIcon(receive.Currency, out var currencyIcon))
                return currencyIcon;
        }

        foreach (var pool in listingProto.ReceivePools)
        {
            if (TryResolveRewardPoolIcon(pool.Pool, out var poolIcon))
                return poolIcon;
        }

        foreach (var cost in listingProto.Cost)
        {
            if (!string.IsNullOrWhiteSpace(cost.Prototype))
                return cost.Prototype;

            if (!string.IsNullOrWhiteSpace(cost.Group) &&
                _prototypes.TryIndex<NcItemGroupPrototype>(cost.Group, out var group) &&
                !string.IsNullOrWhiteSpace(group.Icon) &&
                _prototypes.HasIndex<EntityPrototype>(group.Icon))
                return group.Icon;

            if (!string.IsNullOrWhiteSpace(cost.Currency) && TryResolveCurrencyIcon(cost.Currency, out var currencyIcon))
                return currencyIcon;
        }

        return string.Empty;
    }

    private bool TryResolveRewardPoolIcon(string poolId, out string icon)
    {
        icon = string.Empty;
        if (string.IsNullOrWhiteSpace(poolId) ||
            !_prototypes.TryIndex<NcContractRewardPoolPrototype>(poolId, out var pool))
            return false;

        for (var i = 0; i < pool.Entries.Count; i++)
        {
            var reward = pool.Entries[i];
            var rewardId = GetRewardId(reward);
            if (string.IsNullOrWhiteSpace(rewardId))
                continue;

            if (reward.Type == StoreRewardType.Item && _prototypes.HasIndex<EntityPrototype>(rewardId))
            {
                icon = rewardId;
                return true;
            }

            if (reward.Type == StoreRewardType.Currency && TryResolveCurrencyIcon(rewardId, out icon))
                return true;
        }

        return false;
    }

    private bool TryResolveCurrencyIcon(string currency, out string icon)
    {
        icon = string.Empty;
        if (!_prototypes.TryIndex<StackPrototype>(currency, out var stack) || string.IsNullOrWhiteSpace(stack.Spawn))
            return false;

        if (!_prototypes.HasIndex<EntityPrototype>(stack.Spawn))
            return false;

        icon = stack.Spawn;
        return true;
    }

    private static List<NcBarterCostEntry> CloneBarterCost(List<NcBarterCostEntry> source)
    {
        var result = new List<NcBarterCostEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var c = source[i];
            result.Add(new NcBarterCostEntry
            {
                Prototype = c.Prototype,
                Group = c.Group,
                Currency = c.Currency,
                Count = c.Count
            });
        }

        return result;
    }

    private static List<NcBarterReceiveEntry> CloneBarterReceive(List<NcBarterReceiveEntry> source)
    {
        var result = new List<NcBarterReceiveEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var r = source[i];
            result.Add(new NcBarterReceiveEntry
            {
                Prototype = r.Prototype,
                Currency = r.Currency,
                Count = r.Count
            });
        }

        return result;
    }

    private static List<NcBarterReceivePoolEntry> CloneBarterReceivePools(List<NcBarterReceivePoolEntry> source)
    {
        var result = new List<NcBarterReceivePoolEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var r = source[i];
            result.Add(new NcBarterReceivePoolEntry
            {
                Pool = r.Pool,
                Rolls = r.Rolls,
                Chance = r.Chance
            });
        }

        return result;
    }


    private static IntRange GetRewardAmountRange(ContractRewardDef reward)
    {
        return reward.Count.Min > 0 || reward.Count.Max > 0
            ? reward.Count
            : reward.Amount;
    }
    private static string GetRewardId(ContractRewardDef reward)
    {
        if (!string.IsNullOrWhiteSpace(reward.Prototype))
            return reward.Prototype;

        if (!string.IsNullOrWhiteSpace(reward.Currency))
            return reward.Currency;

        if (!string.IsNullOrWhiteSpace(reward.Pool))
            return reward.Pool;

        return reward.Id;
    }

    private static int CountNonEmpty(params string[] values)
    {
        var count = 0;
        for (var i = 0; i < values.Length; i++)
            if (!string.IsNullOrWhiteSpace(values[i]))
                count++;

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
