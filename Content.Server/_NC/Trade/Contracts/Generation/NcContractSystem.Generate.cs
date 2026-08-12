using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateContractData(EntityUid store, ContractPoolCandidate candidate)
    {
        var contract = TryGetDefinitionHandler(candidate.Kind, out var handler)
            ? handler.CreateContract(this, store, candidate)
            : CreateInvalidContractData(candidate);

        contract.OfferPoolId = candidate.OfferPoolId;
        contract.OfferPoolName = candidate.OfferPoolName;
        contract.OfferPoolOrder = candidate.OfferPoolOrder;
        contract.OfferPoolColor = candidate.OfferPoolColor;
        return contract;
    }

    private static ContractServerData CreateInvalidContractData(ContractPoolCandidate candidate) =>
        new()
        {
            Id = candidate.Id,
            Name = candidate.Id,
            Description = "Invalid contract candidate.",
            Repeatable = candidate.Repeatable,
            ObjectiveType = ContractObjectiveType.Delivery,
            ExecutionKind = ContractExecutionKind.InventoryDelivery,
            FlowStatus = ContractFlowStatus.Failed
        };

    private static int CalculateTotalRequired(List<ContractTargetServerData> targets)
    {
        var totalRequired = 0;
        foreach (var target in targets)
            totalRequired = SaturatingAdd(totalRequired, Math.Max(0, target.Required));

        return totalRequired;
    }

    private static string GetPrimaryTargetId(List<ContractTargetServerData> targets) =>
        targets.Count > 0 ? targets[0].TargetItem : string.Empty;
}
