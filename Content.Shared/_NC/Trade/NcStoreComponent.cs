using Robust.Shared.GameStates;


namespace Content.Shared._NC.Trade;
public readonly record struct StoreListingKey(StoreMode Mode, string ListingId);

[RegisterComponent, NetworkedComponent]
public sealed partial class NcStoreComponent : Component
{
    public EntityUid? CurrentUser;

    public int UiRevision;

    [ViewVariables]
    public HashSet<string> CompletedOneTimeContracts { get; } = new();

    [DataField("categories")]
    public List<string> Categories { get; set; } = new();

    [DataField("currencyWhitelist")]
    public List<string> CurrencyWhitelist { get; set; } = new();

    public List<StoreListingPrototype> Listings { get; set; } = new();

    /// <summary>
    ///     Fast lookup cache for <see cref="Listings" />.
    ///     Key format: "{(byte)mode}:{listingId}".
    ///     Not networked; rebuilt on server when presets are (re)loaded.
    /// </summary>
    [ViewVariables]
    public Dictionary<StoreListingKey, StoreListingPrototype> ListingIndex { get; } = new();

    [DataField("preset")]
    public string? LegacyPreset { get; set; }

    [DataField("buyPresets")]
    public List<string> BuyPresets { get; set; } = new();

    [DataField("sellPresets")]
    public List<string> SellPresets { get; set; } = new();

    [DataField("contractPresets")]
    public List<string> ContractPresets { get; set; } = new();

    [DataField("contracts")]
    public string? LegacyContractsPreset { get; set; }

    public Dictionary<string, ContractServerData> Contracts { get; } = new();

    [DataField("rewardCurrencies")]
    public Dictionary<string, int> RewardCurrencies { get; set; } = new();

    [DataField("rewardItems")]
    public Dictionary<string, int> RewardItems { get; set; } = new();

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
