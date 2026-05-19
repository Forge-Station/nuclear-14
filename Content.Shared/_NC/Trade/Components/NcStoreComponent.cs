using Robust.Shared.GameStates;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;


namespace Content.Shared._NC.Trade;


public readonly record struct StoreListingKey(StoreMode Mode, string ListingId);

[RegisterComponent, NetworkedComponent]
public sealed partial class NcStoreComponent : Component
{
    // Older Corvax maps only stored generated categories/currencies.
    // If the map has no profile yet, load it as the city trade profile and save back in the new compact format.
    [DataField("profile")]
    public ProtoId<NcStoreProfilePrototype> Profile { get; set; } = "TrademachineCityProfile";

    public int CatalogRevision;
    public EntityUid? CurrentUser;
    public int UiRevision;

    [ViewVariables]
    public HashSet<string> CompletedOneTimeContracts { get; } = new();

    [ViewVariables]
    public List<string> Categories { get; } = new();

    [ViewVariables]
    public List<string> CurrencyWhitelist { get; } = new();

    // Map-save bridge: old maps may contain these runtime caches, but stores rebuild them from Profile.
    [DataField("categories", readOnly: true)]
    private List<string> MapSaveCategoriesBridge = new();

    [DataField("currencyWhitelist", readOnly: true)]
    private List<string> MapSaveCurrencyWhitelistBridge = new();

    public List<NcStoreListingDef> Listings { get; set; } = new();

    [ViewVariables]
    public Dictionary<StoreListingKey, NcStoreListingDef> ListingIndex { get; } = new();

    public Dictionary<string, ContractServerData> Contracts { get; } = new();

    public void BumpCatalogRevision() => CatalogRevision = unchecked(CatalogRevision + 1);

    public static StoreListingKey MakeListingKey(StoreMode mode, string listingId) => new(mode, listingId);

    public void RebuildListingIndex()
    {
        ListingIndex.Clear();
        foreach (var l in Listings)
        {
            if (string.IsNullOrWhiteSpace(l.Id))
                continue;

            var key = MakeListingKey(l.Mode, l.Id);
            if (ListingIndex.ContainsKey(key))
            {
                Logger.GetSawmill("ncstore").Error($"[NcStore] Duplicate listing id '{l.Id}' for mode '{l.Mode}' was ignored while rebuilding index.");
                continue;
            }

            ListingIndex[key] = l;
        }
    }
}
