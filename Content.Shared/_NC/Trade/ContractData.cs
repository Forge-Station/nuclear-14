namespace Content.Shared._NC.Trade;

[Serializable]
public sealed class ContractServerData
{
    public string Id { get; set; } = string.Empty;
    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }

    public int Reward { get; set; }
    public string RewardCurrency { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Easy";

    public string? RewardItem = null;
    public int RewardItemCount = 0;
    public string Description { get; set; } = string.Empty;

    public bool Completed => Progress >= Required;
}
