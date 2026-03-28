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
            contract.Difficulty,
            contract.Description,
            contract.Repeatable,
            contract.Taken,
            SupportsContractPinpointer(contract),
            contract.ExecutionKind,
            CloneRuntimeContext(EnsureClientContractRuntime(contract)),
            contract.FlowStatus,
            contract.Completed,
            contract.TargetItem,
            ResolveContractTurnInItem(contract),
            contract.Required,
            contract.Progress,
            targets,
            rewards
        );
    }

    private static List<ContractTargetClientData> MapContractTargetsToClient(ContractServerData contract)
    {
        var sourceTargets = EnsureClientContractTargets(contract);
        var targets = sourceTargets is { Count: > 0 }
            ? new List<ContractTargetClientData>(sourceTargets.Count)
            : new List<ContractTargetClientData>(1);

        if (sourceTargets is { Count: > 0 })
        {
            foreach (var target in sourceTargets)
            {
                if (string.IsNullOrWhiteSpace(target.TargetItem) || target.Required <= 0)
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
        var rewards = EnsureClientContractRewards(contract);
        return rewards.Count > 0
            ? new List<ContractRewardData>(rewards)
            : new List<ContractRewardData>(0);
    }

    private static string ResolveContractTurnInItem(ContractServerData contract)
    {
        var config = EnsureClientContractConfig(contract);
        return config.ProofPrototype ?? string.Empty;
    }

    private static bool SupportsContractPinpointer(ContractServerData contract)
    {
        var config = EnsureClientContractConfig(contract);
        if (!config.GivePinpointer)
            return false;

        return contract.UsesWorldObjectiveRuntime;
    }

    private static ContractRuntimeContextData EnsureClientContractRuntime(ContractServerData contract)
    {
        contract.Runtime ??= new();
        return contract.Runtime;
    }

    private static ContractObjectiveConfigData EnsureClientContractConfig(ContractServerData contract)
    {
        contract.Config ??= new();
        return contract.Config;
    }

    private static List<ContractTargetServerData> EnsureClientContractTargets(ContractServerData contract)
    {
        contract.Targets ??= new();
        for (var i = contract.Targets.Count - 1; i >= 0; i--)
        {
            if (contract.Targets[i] == null)
                contract.Targets.RemoveAt(i);
        }

        return contract.Targets;
    }

    private static List<ContractRewardData> EnsureClientContractRewards(ContractServerData contract)
    {
        contract.Rewards ??= new();
        return contract.Rewards;
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
            GhostRolePendingAcceptance = runtime.GhostRolePendingAcceptance,
            Failed = runtime.Failed,
            FailureReason = runtime.FailureReason
        };
    }
}
