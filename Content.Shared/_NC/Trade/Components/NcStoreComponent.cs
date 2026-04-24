using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;


namespace Content.Shared._NC.Trade;


public readonly record struct StoreListingKey(StoreMode Mode, string ListingId);

[RegisterComponent, NetworkedComponent]
public sealed partial class NcStoreComponent : Component
{
    [DataField("profile", required: true)]
    public ProtoId<NcStoreProfilePrototype> Profile { get; set; } = default!;

    public int CatalogRevision;
    public EntityUid? CurrentUser;
    public int UiRevision;

    [ViewVariables]
    public HashSet<string> CompletedOneTimeContracts { get; } = new();

    [ViewVariables]
    public List<string> Categories { get; } = new();

    [ViewVariables]
    public List<string> CurrencyWhitelist { get; } = new();

    public List<NcStoreListingDef> Listings { get; set; } = new();

    [ViewVariables]
    public Dictionary<StoreListingKey, NcStoreListingDef> ListingIndex { get; } = new();

    public Dictionary<string, ContractServerData> Contracts { get; } = new();

    // Legacy map compatibility: old snapshots may still contain these fields on NcStore.
    [DataField("categories")]
    private List<string>? LegacyCategories { set { } }

    [DataField("currencyWhitelist")]
    private List<string>? LegacyCurrencyWhitelist { set { } }

    public void BumpCatalogRevision() => CatalogRevision = unchecked(CatalogRevision + 1);

    public static StoreListingKey MakeListingKey(StoreMode mode, string listingId) => new(mode, listingId);

    public void RebuildListingIndex()
    {
        ListingIndex.Clear();
        foreach (var l in Listings)
        {
            if (string.IsNullOrWhiteSpace(l.Id))
                continue;

            ListingIndex[MakeListingKey(l.Mode, l.Id)] = l;
        }
    }
}
