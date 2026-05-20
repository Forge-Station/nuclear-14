using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class StoreSystemStructuredLoader
{
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
            AddRewardPoolCurrenciesToWhitelist(comp, ctx, pool.Pool, new HashSet<string>(StringComparer.Ordinal), 0);
    }

    private void AddRewardPoolCurrenciesToWhitelist(
        NcStoreComponent comp,
        LoadContext ctx,
        string poolId,
        HashSet<string> visited,
        int depth)
    {
        if (string.IsNullOrWhiteSpace(poolId) || depth > MaxRewardPoolTraversalDepth)
            return;

        if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(poolId, out var supplyPool))
            return;

        if (!visited.Add(poolId))
            return;

        for (var i = 0; i < supplyPool.Entries.Count; i++)
        {
            var reward = supplyPool.Entries[i];
            if (reward.Type == StoreRewardType.Pool)
            {
                AddRewardPoolCurrenciesToWhitelist(comp, ctx, reward.Pool, visited, depth + 1);
                continue;
            }

            if (reward.Type != StoreRewardType.Currency)
                continue;

            if (!string.IsNullOrWhiteSpace(reward.Currency) && ctx.CurrencySeen.Add(reward.Currency))
                comp.CurrencyWhitelist.Add(reward.Currency);
        }

        visited.Remove(poolId);
    }
}
