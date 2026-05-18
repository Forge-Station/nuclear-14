using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem
{
    private ContractClientData MapContractToClient(ContractServerData contract)
    {
        var targets = MapContractTargetsToClient(contract);
        var rewards = CloneContractRewards(contract);

        return new(
            contract.Id,
            contract.Name,
            contract.Description,
            contract.Repeatable,
            contract.Taken,
            SupportsContractPinpointer(contract),
            contract.ExecutionKind,
            CloneRuntimeContext(contract.Runtime),
            contract.FlowStatus,
            contract.Completed,
            contract.TargetItem,
            ResolveContractTurnInItem(contract),
            contract.Required,
            contract.Progress,
            targets,
            rewards,
            contract.Config.RetrievalSourceHint,
            contract.Config.RetrievalDestinationHint,
            IsRetrievalRouteContract(contract),
            contract.Config.RetrievalClaimMode,
            IsRetrievalBearerProofContract(contract),
            contract.Config.HuntV2CompletionMode,
            contract.Config.GhostRoleCompletionMode,
            contract.OfferPoolId,
            contract.OfferPoolName,
            contract.OfferPoolOrder,
            contract.OfferPoolColor
        );
    }

    private static List<ContractTargetClientData> MapContractTargetsToClient(ContractServerData contract)
    {
        var sourceTargets = contract.Targets;
        var targets = sourceTargets is { Count: > 0 }
            ? new List<ContractTargetClientData>(sourceTargets.Count)
            : new List<ContractTargetClientData>(1);

        if (sourceTargets is { Count: > 0 })
        {
            foreach (var target in sourceTargets)
            {
                if (target == null || string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
                    continue;

                targets.Add(
                    new(target.TargetItem, target.Required, target.Progress)
                    {
                        MatchMode = target.MatchMode
                    });
            }

            return targets;
        }

        if (!string.IsNullOrWhiteSpace(contract.TargetItem) && contract.Required > 0)
        {
            targets.Add(
                new(contract.TargetItem, contract.Required, contract.Progress)
                {
                    MatchMode = contract.MatchMode
                });
        }

        return targets;
    }

    private static List<ContractRewardData> CloneContractRewards(ContractServerData contract)
    {
        var rewards = contract.Rewards;
        return rewards.Count > 0
            ? new List<ContractRewardData>(rewards)
            : new List<ContractRewardData>(0);
    }

    private static string ResolveContractTurnInItem(ContractServerData contract)
    {
        var config = contract.Config;
        if (contract.IsHuntObjective &&
            config.HuntV2Enabled &&
            config.HuntV2CompletionMode == NcHuntCompletionMode.BodyTurnIn)
        {
            return config.HuntV2BodyPrototype ?? string.Empty;
        }

        return config.ProofPrototype ?? string.Empty;
    }

    private static bool SupportsContractPinpointer(ContractServerData contract)
    {
        var config = contract.Config;
        if (!config.GivePinpointer)
            return false;

        if (SupportsRetrievalSpawnedPinpointer(contract))
            return true;

        return contract.UsesWorldObjectiveRuntime;
    }

    private static bool SupportsRetrievalSpawnedPinpointer(ContractServerData contract)
    {
        var config = contract.Config;
        return contract.IsInventoryDelivery &&
               config.RetrievalSpawnEnabled &&
               config.RetrievalRequireSpawnedEntities;
    }

    private static bool IsRetrievalRouteContract(ContractServerData contract)
    {
        return contract.IsInventoryDelivery &&
               !string.IsNullOrWhiteSpace(contract.Config.RetrievalRouteId);
    }

    private static bool IsRetrievalBearerProofContract(ContractServerData contract)
    {
        var config = contract.Config;
        return IsRetrievalRouteContract(contract) &&
               config.RetrievalProofEnabled &&
               config.RetrievalProofOwnership == NcRetrievalProofOwnership.Bearer;
    }

    private static ContractRuntimeContextData CloneRuntimeContext(ContractRuntimeContextData? runtime)
    {
        if (runtime == null)
            return new ContractRuntimeContextData();

        return new ContractRuntimeContextData
        {
            Stage = runtime.Stage,
            StageGoal = runtime.StageGoal,
            AcceptTimeoutRemainingSeconds = runtime.AcceptTimeoutRemainingSeconds,
            GhostRoleSurvivalRemainingSeconds = runtime.GhostRoleSurvivalRemainingSeconds,
            GhostRolePendingAcceptance = runtime.GhostRolePendingAcceptance,
            Failed = runtime.Failed,
            FailureReason = runtime.FailureReason,
            StatusHint = runtime.StatusHint
        };
    }
}
