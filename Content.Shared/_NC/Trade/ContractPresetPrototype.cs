using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Trade;

[Prototype("storeContractsPreset")]
public sealed partial class StoreContractsPresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("contracts", required: true)]
    public List<string> Contracts { get; set; } = new();
}
