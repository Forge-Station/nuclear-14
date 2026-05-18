using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

/// <summary>
/// Trade contracts Supply reward entry. Unified format:
/// reward:
/// - type: Currency / Item / Pool
///   currency/prototype/pool: ...
///   count: 1 or { min, max }
/// </summary>
[DataDefinition]
public sealed partial class NcSupplyRewardEntry
{
    [DataField("type", required: true)]
    public StoreRewardType Type { get; set; } = StoreRewardType.Unspecified;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    [DataField("pool")]
    public string Pool { get; set; } = string.Empty;

    [DataField("count", required: true)]
    public IntRange Count { get; set; } = IntRange.Fixed(0);
}

/// <summary>
/// Strict Trade reward pool shared by Trade contracts and Barter receivePools.
/// Use count + weight only; entries target prototype/currency/pool by reward type.
/// Old id/amount/prob/chance/options aliases are intentionally not represented.
/// The audit rejects them so reward YAML stays on the strict type + id + count shape.
/// </summary>
[Prototype("ncSupplyRewardPool")]
public sealed partial class NcSupplyRewardPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("entries", required: true)]
    public List<NcSupplyRewardPoolEntry> Entries { get; private set; } = new();
}

[DataDefinition]
public sealed partial class NcSupplyRewardPoolEntry
{
    [DataField("type", required: true)]
    public StoreRewardType Type { get; set; } = StoreRewardType.Unspecified;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    [DataField("pool")]
    public string Pool { get; set; } = string.Empty;

    [DataField("count", required: true)]
    public IntRange Count { get; set; } = IntRange.Fixed(0);

    [DataField("weight")]
    public int Weight { get; set; } = 1;

    [DataField("max")]
    public int MaxRepeats { get; set; } = 0;
}

