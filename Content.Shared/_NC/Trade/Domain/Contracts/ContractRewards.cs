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

public sealed partial class ContractRewardDef
{
    public StoreRewardType Type { get; set; } = StoreRewardType.Item;

    public string RewardId { get; set; } = string.Empty;

    public IntRange Count { get; set; } = IntRange.Fixed(1);

    public int Weight { get; set; } = 1;

    public int MaxRepeats { get; set; } = 0;
}

[Serializable, NetSerializable]
public readonly record struct ContractRewardData(StoreRewardType Type, string Id, int Amount);
