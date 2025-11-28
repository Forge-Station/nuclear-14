using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class ContractUiState : BoundUserInterfaceState
{
    public readonly List<ContractClientData> Contracts;

    public ContractUiState(List<ContractClientData> contracts)
    {
        Contracts = contracts;
    }
}

[Serializable, NetSerializable,]
public readonly record struct ContractClientData(
    string Id,
    string TargetItem,
    int Progress,
    int Required,
    int Reward,
    string RewardCurrency,
    string Difficulty,
    bool Completed,
    string Description
);


