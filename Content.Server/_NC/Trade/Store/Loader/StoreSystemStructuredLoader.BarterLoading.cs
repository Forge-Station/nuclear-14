using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class StoreSystemStructuredLoader
{
    private int LoadBarterPreset(
        ProtoId<NcBarterPresetPrototype> presetId,
        NcStoreComponent comp,
        LoadContext ctx
    )
    {
        if (!_prototypes.TryIndex<NcBarterPresetPrototype>(presetId, out var preset))
        {
            Sawmill.Warning($"[NcStore] Barter preset '{presetId}' not found.");
            return 0;
        }

        var count = 0;

        foreach (var categoryId in preset.Categories)
        {
            if (!_prototypes.TryIndex(categoryId, out var categoryProto))
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
                if (!_prototypes.TryIndex(listingId, out var listingProto))
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
        LoadContext ctx
    )
    {
        if (!ValidateBarterListing(listingProto, presetId, categoryId))
            return 0;

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
            Categories = new() { categoryName, },
            Conditions = new(),
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
}
