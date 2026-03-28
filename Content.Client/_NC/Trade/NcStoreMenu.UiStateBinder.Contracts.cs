using Content.Shared._NC.Trade;

namespace Content.Client._NC.Trade;

public sealed partial class NcStoreMenu
{
    private sealed partial class UiStateBinder
    {
        private static int ComputeContractsHash(List<ContractClientData> contracts)
        {
            unchecked
            {
                var h = 17;
                for (var i = 0; i < contracts.Count; i++)
                    h = AppendContractHash(h, contracts[i]);

                return h;
            }
        }

        private static int AppendContractHash(int hash, ContractClientData contract)
        {
            unchecked
            {
                var h = hash;
                h = h * 31 + (contract.Id?.GetHashCode() ?? 0);
                h = h * 31 + (contract.Name?.GetHashCode() ?? 0);
                h = h * 31 + (contract.Difficulty?.GetHashCode() ?? 0);
                h = h * 31 + (contract.Description?.GetHashCode() ?? 0);
                h = h * 31 + (contract.TargetItem?.GetHashCode() ?? 0);
                h = h * 31 + (contract.TurnInItem?.GetHashCode() ?? 0);
                h = h * 31 + (contract.Repeatable ? 1 : 0);
                h = h * 31 + (contract.Taken ? 1 : 0);
                h = h * 31 + (int) contract.ExecutionKind;
                h = h * 31 + (int) contract.FlowStatus;
                h = h * 31 + (contract.Completed ? 1 : 0);
                h = h * 31 + contract.Progress;
                h = h * 31 + contract.Required;
                h = h * 31 + (contract.SupportsPinpointer ? 1 : 0);
                h = AppendRuntimeHash(h, contract.Runtime);
                h = AppendTargetsHash(h, contract.Targets);
                h = AppendRewardsHash(h, contract.Rewards);
                return h;
            }
        }

        private static int AppendRuntimeHash(int hash, ContractRuntimeContextData? runtime)
        {
            unchecked
            {
                var h = hash;
                if (runtime == null)
                    return h;

                h = h * 31 + runtime.Stage;
                h = h * 31 + runtime.StageGoal;
                h = h * 31 + runtime.AcceptTimeoutRemainingSeconds;
                h = h * 31 + (runtime.GhostRolePendingAcceptance ? 1 : 0);
                h = h * 31 + (runtime.Failed ? 1 : 0);
                h = h * 31 + (runtime.FailureReason?.GetHashCode() ?? 0);
                return h;
            }
        }

        private static int AppendTargetsHash(int hash, List<ContractTargetClientData>? targets)
        {
            unchecked
            {
                var h = hash * 31 + (targets?.Count ?? 0);
                if (targets == null)
                    return h;

                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    h = h * 31 + (target.TargetItem?.GetHashCode() ?? 0);
                    h = h * 31 + target.Required;
                    h = h * 31 + target.Progress;
                    h = h * 31 + (int) target.MatchMode;
                }

                return h;
            }
        }

        private static int AppendRewardsHash(int hash, List<ContractRewardData>? rewards)
        {
            unchecked
            {
                var h = hash * 31 + (rewards?.Count ?? 0);
                if (rewards == null)
                    return h;

                for (var i = 0; i < rewards.Count; i++)
                {
                    var reward = rewards[i];
                    h = h * 31 + (int) reward.Type;
                    h = h * 31 + (reward.Id?.GetHashCode() ?? 0);
                    h = h * 31 + reward.Amount;
                }

                return h;
            }
        }
    }
}
