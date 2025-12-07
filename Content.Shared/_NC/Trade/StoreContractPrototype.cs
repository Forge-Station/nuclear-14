using Robust.Shared.Prototypes;


namespace Content.Shared._NC.Trade;


[Prototype("storeContract")]
public sealed class StoreContractPrototype : IPrototype
{
    [DataField("description")]
    public string Description = string.Empty;


    [DataField("difficulty")]
    public string Difficulty = "Easy";


    [DataField("name", required: true)]
    public string Name = string.Empty;


    [DataField("required")]
    public int Required = 1;


    [DataField("reward")]
    public int Reward;


    [DataField("rewardCurrency")]
    public string RewardCurrency = string.Empty;

    [DataField("rewardItem")]
    public string? RewardItem;

    [DataField("rewardItemCount")]
    public int RewardItemCount;


    [DataField("targetItem", required: true)]
    public string TargetItem = string.Empty;

    [IdDataField]
    public string ID { get; private set; } = default!;
}
