using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;



[Serializable, NetSerializable]
public enum StoreRewardType : byte
{
    Item = 0,
    Currency = 1,
    Pool = 2,

    /// <summary>Sentinel used by strict contract validation when a YAML entry omits type.</summary>
    Unspecified = byte.MaxValue
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class ContractRewardDef
{
    [DataField("type")]
    public StoreRewardType Type { get; set; } = StoreRewardType.Item;

    /// <summary>Legacy generic reward id. Prefer prototype/currency/pool in new Trade contracts YAML.</summary>
    [DataField("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Trade contracts alias for Item rewards.</summary>
    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    /// <summary>Trade contracts alias for Currency rewards.</summary>
    [DataField("currency")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>Trade contracts alias for Pool rewards.</summary>
    [DataField("pool")]
    public string Pool { get; set; } = string.Empty;

    [DataField("amount")]
    public IntRange Amount { get; set; } = IntRange.Fixed(1);

    /// <summary>Readable amount alias used by newer Trade YAML. If set, this overrides amount.</summary>
    [DataField("count")]
    public IntRange Count { get; set; } = IntRange.Fixed(0);

    /// <summary>Legacy probability field.</summary>
    [DataField("prob")]
    public float Probability { get; set; } = 1.0f;

    /// <summary>Trade contracts readable probability alias. Set to 0..1 to override prob; negative means unset.</summary>
    [DataField("chance")]
    public float Chance { get; set; } = -1.0f;

    [DataField("weight")]
    public int Weight { get; set; } = 1;

    [DataField("max")]
    public int MaxRepeats { get; set; } = 0;

    [DataField("options")]
    public List<ContractRewardDef>? Options { get; set; }
}

[Serializable, NetSerializable]
public readonly record struct ContractRewardData(StoreRewardType Type, string Id, int Amount);
