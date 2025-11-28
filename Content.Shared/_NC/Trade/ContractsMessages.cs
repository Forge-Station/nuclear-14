using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class ClaimContractBoundMessage : BoundUserInterfaceMessage
{
    public ClaimContractBoundMessage(string id)
    {
        ContractId = id;
    }

    public string ContractId { get; }
}

[Serializable, NetSerializable,]
public sealed class RequestContractsRefreshMessage : BoundUserInterfaceMessage { }
