using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

public sealed class ContractServerData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public List<ContractTargetServerData> Targets { get; set; } = new();

    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }

    public bool Repeatable { get; set; } = true;
    public bool Taken { get; set; }
    public ContractObjectiveType ObjectiveType { get; set; } = ContractObjectiveType.Delivery;
    public ContractRuntimeContextData Runtime { get; set; } = new();
    public ContractObjectiveConfigData Config { get; set; } = new();
    public ContractFlowStatus FlowStatus { get; set; } = ContractFlowStatus.Available;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string Difficulty { get; set; } = "Easy";
    public string Description { get; set; } = string.Empty;

    public List<ContractRewardData> Rewards { get; set; } = new();

    public bool Completed
    {
        get
        {
            if (ObjectiveType != ContractObjectiveType.Delivery)
                return Required > 0 && Progress >= Required;

            if (Targets.Count > 0)
            {
                var any = false;
                foreach (var t in Targets)
                {
                    if (t.Required <= 0)
                        continue;

                    any = true;
                    if (t.Progress < t.Required)
                        return false;
                }

                return any;
            }

            return Required > 0 && Progress >= Required;
        }
    }
}
