using System;
using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateContractData(EntityUid store, ContractPoolCandidate candidate)
    {
        var contract = candidate.Kind switch
        {
            ContractPoolCandidateKind.SupplyV2 when candidate.Supply != null => CreateSupplyContractData(store, candidate.Supply),
            ContractPoolCandidateKind.RetrievalV2 when candidate.Retrieval != null => CreateRetrievalContractData(store, candidate.Retrieval),
            ContractPoolCandidateKind.HuntV2 when candidate.Hunt != null => CreateHuntContractData(store, candidate.Hunt),
            ContractPoolCandidateKind.GhostRoleV2 when candidate.GhostRole != null => CreateGhostRoleContractData(store, candidate.GhostRole),
            _ => CreateInvalidContractData(candidate)
        };

        contract.OfferPoolId = candidate.OfferPoolId;
        contract.OfferPoolName = candidate.OfferPoolName;
        contract.OfferPoolOrder = candidate.OfferPoolOrder;
        contract.OfferPoolColor = candidate.OfferPoolColor;
        return contract;
    }

    private static ContractServerData CreateInvalidContractData(ContractPoolCandidate candidate)
    {
        return new ContractServerData
        {
            Id = candidate.Id,
            Name = candidate.Id,
            Description = "Invalid contract candidate.",
            Repeatable = candidate.Repeatable,
            ObjectiveType = ContractObjectiveType.Delivery,
            FlowStatus = ContractFlowStatus.Failed
        };
    }

    private static int CalculateTotalRequired(List<ContractTargetServerData> targets)
    {
        var totalRequired = 0;
        foreach (var target in targets)
            totalRequired = SaturatingAdd(totalRequired, Math.Max(0, target.Required));

        return totalRequired;
    }

    private static string GetPrimaryTargetId(List<ContractTargetServerData> targets)
    {
        return targets.Count > 0 ? targets[0].TargetItem : string.Empty;
    }

}
