using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable, Prototype("ncStoreListing")]
public sealed class StoreListingPrototype : IPrototype
{
    [IdDataField]
    public string Id = string.Empty;


    [DataField("mode")]
    public StoreMode Mode = StoreMode.Buy;


    [DataField("productEntity")]
    public string ProductEntity = string.Empty;


    [DataField("cost")]
    public Dictionary<string, int> Cost { get; set; } = new();


    [DataField("categories")]
    public List<string> Categories { get; set; } = new();


    [DataField("conditions")]
    public List<ListingConditionPrototype> Conditions { get; set; } = new();


    [ViewVariables(VVAccess.ReadWrite)]
    public int RemainingCount { get; set; } = -1;

    public string ID => Id;
}
