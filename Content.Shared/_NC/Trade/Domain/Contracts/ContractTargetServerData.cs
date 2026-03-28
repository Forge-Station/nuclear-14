using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable]
public sealed class ContractTargetServerData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }
}
