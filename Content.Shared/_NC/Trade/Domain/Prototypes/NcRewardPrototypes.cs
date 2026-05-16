using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

/// <summary>
/// ContractsV2 Supply reward entry. Unified format:
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

    /// <summary>Legacy trap: Supply V2 reward entries must use count, not amount.</summary>
    [DataField("amount")]
    public IntRange LegacyAmount { get; set; } = IntRange.Fixed(int.MinValue);

    /// <summary>Legacy trap: Supply V2 uses pool weight / count range, not per-entry probability.</summary>
    [DataField("prob")]
    public float LegacyProbability { get; set; } = float.NaN;

    /// <summary>Legacy trap: Supply V2 uses pool weight / count range, not per-entry chance.</summary>
    [DataField("chance")]
    public float LegacyChance { get; set; } = float.NaN;

    /// <summary>Legacy trap: use prototype/currency/pool depending on type, not id.</summary>
    [DataField("id")]
    public string LegacyId { get; set; } = string.Empty;

    /// <summary>Legacy trap: nested option lists are not part of Supply V2 rewards.</summary>
    [DataField("options")]
    public List<ContractRewardDef>? LegacyOptions { get; set; }
}

/// <summary>
/// Legacy/general reward pool used by old storeContract rewards and Barter receivePools.
/// Keep this format tolerant because legacy contracts may still use id/amount/prob/options.
/// Supply V2 must use ncSupplyRewardPool instead.
/// </summary>
[Prototype("ncContractRewardPool")]
public sealed partial class NcContractRewardPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("entries")]
    public List<ContractRewardDef> Entries { get; private set; } = new();
}

/// <summary>
/// Strict Supply V2 reward pool. This format intentionally has no legacy id/amount/prob/chance/options fields.
/// Use count + weight only; nested pools are rejected by Supply validation.
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

    [DataField("count", required: true)]
    public IntRange Count { get; set; } = IntRange.Fixed(0);

    [DataField("weight")]
    public int Weight { get; set; } = 1;

    [DataField("max")]
    public int MaxRepeats { get; set; } = 0;

    /// <summary>Legacy trap: Supply V2 reward pool entries must use count, not amount.</summary>
    [DataField("amount")]
    public IntRange LegacyAmount { get; set; } = IntRange.Fixed(int.MinValue);

    /// <summary>Legacy trap: Supply V2 reward pools use weight, not prob.</summary>
    [DataField("prob")]
    public float LegacyProbability { get; set; } = float.NaN;

    /// <summary>Legacy trap: Supply V2 reward pools use weight, not chance.</summary>
    [DataField("chance")]
    public float LegacyChance { get; set; } = float.NaN;

    /// <summary>Legacy trap: use prototype/currency depending on type, not id.</summary>
    [DataField("id")]
    public string LegacyId { get; set; } = string.Empty;

    /// <summary>Legacy trap: nested pools are rejected explicitly by Supply V2 validation.</summary>
    [DataField("pool")]
    public string LegacyPool { get; set; } = string.Empty;

    /// <summary>Legacy trap: nested option lists are not part of Supply V2 reward pools.</summary>
    [DataField("options")]
    public List<ContractRewardDef>? LegacyOptions { get; set; }
}



