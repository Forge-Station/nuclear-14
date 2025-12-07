using Robust.Shared.Prototypes;


namespace Content.Shared._NC.Trade;


[Prototype("storeContractsPreset")]
public sealed class StoreContractsPresetPrototype : IPrototype
{
    [DataField("contracts", required: true)]
    public List<string> Contracts { get; set; } = new();

    [IdDataField]
    public string ID { get; private set; } = default!;
}
