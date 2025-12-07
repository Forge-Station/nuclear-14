using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Trade;

[Prototype("storePresetStructured")]
public sealed partial class StorePresetStructuredPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;


    [DataField("currency", required: true)]
    public string Currency = string.Empty;


    [DataField("catalog", required: true)]
    public Dictionary<string, List<StoreCatalogEntry>> Catalog = new();

    [DataDefinition]
    public sealed partial class StoreCatalogEntry
    {
        [DataField("price", required: true)]
        public int Price;

        [DataField("proto", required: true)]
        public string Proto = string.Empty;

        [DataField("count")]
        public int? Count { get; set; }
    }
}
