namespace Content.Shared._NC.Trade;


[Serializable]
[DataDefinition]
public sealed partial class ContractTargetServerData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }
    public bool BodyRequired { get; set; }
}
