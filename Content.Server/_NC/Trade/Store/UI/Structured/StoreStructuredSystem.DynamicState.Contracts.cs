using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    private void PopulateDynamicContracts(
        EntityUid store,
        NcStoreComponent comp,
        bool hasContractsTab,
        DynamicScratch scratch,
        DynamicStateBuffer buf)
    {
        if (!hasContractsTab || comp.Contracts.Count == 0)
            return;

        var signature = ComputeContractsSignature(store, comp, scratch.ContractsSignatureScratch);
        if (scratch.TryPopulateCachedContracts(signature, buf))
            return;

        foreach (var contract in comp.Contracts.Values)
            buf.Contracts.Add(MapContractToClient(store, contract));

        buf.Contracts.Sort(CompareContractsForUi);
        scratch.CacheContracts(signature, buf.Contracts);
    }

    private int ComputeContractsSignature(
        EntityUid store,
        NcStoreComponent comp,
        List<ContractServerData> contractsScratch)
    {
        unchecked
        {
            contractsScratch.Clear();
            contractsScratch.AddRange(comp.Contracts.Values);
            contractsScratch.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));

            var hash = 17;
            AddHash(ref hash, contractsScratch.Count);
            for (var i = 0; i < contractsScratch.Count; i++)
                AddHash(ref hash, ComputeContractSignature(store, contractsScratch[i]));

            contractsScratch.Clear();
            return hash;
        }
    }

    private int ComputeContractSignature(EntityUid store, ContractServerData contract)
    {
        unchecked
        {
            var hash = 17;
            AddHash(ref hash, contract.Id);
            AddHash(ref hash, contract.Name);
            AddHash(ref hash, contract.Description);
            AddHash(ref hash, contract.Repeatable);
            AddHash(ref hash, contract.Taken);
            AddHash(ref hash, SupportsContractPinpointer(contract));
            AddHash(ref hash, _contracts.CanPartiallyTurnInNow(store, contract.Id, contract));
            AddHash(ref hash, contract.ExecutionKind);
            AddHash(ref hash, contract.FlowStatus);
            AddHash(ref hash, contract.Completed);
            AddHash(ref hash, contract.TargetItem);
            AddHash(ref hash, contract.MatchMode);
            AddHash(ref hash, ResolveContractTurnInItem(contract));
            AddHash(ref hash, contract.Required);
            AddHash(ref hash, contract.Progress);
            AddHash(ref hash, contract.Config.RetrievalSourceHint);
            AddHash(ref hash, contract.Config.RetrievalDestinationHint);
            AddHash(ref hash, IsRetrievalRouteContract(contract));
            AddHash(ref hash, contract.Config.RetrievalClaimMode);
            AddHash(ref hash, IsRetrievalBearerProofContract(contract));
            AddHash(ref hash, contract.Config.HuntCompletionMode);
            AddHash(ref hash, contract.Config.GhostRoleCompletionMode);
            AddHash(ref hash, contract.OfferPoolId);
            AddHash(ref hash, contract.OfferPoolName);
            AddHash(ref hash, contract.OfferPoolOrder);
            AddHash(ref hash, contract.OfferPoolColor);
            AddRuntimeHash(ref hash, contract.Runtime);
            AddTargetsHash(ref hash, contract.Targets);
            AddRewardsHash(ref hash, contract.Rewards);
            return hash;
        }
    }

    private static void AddRuntimeHash(ref int hash, ContractRuntimeContextData? runtime)
    {
        if (runtime == null)
        {
            AddHash(ref hash, 0);
            return;
        }

        AddHash(ref hash, runtime.Stage);
        AddHash(ref hash, runtime.StageGoal);
        AddHash(ref hash, runtime.AcceptTimeoutRemainingSeconds);
        AddHash(ref hash, runtime.GhostRoleSurvivalRemainingSeconds);
        AddHash(ref hash, runtime.GhostRolePendingAcceptance);
        AddHash(ref hash, runtime.Failed);
        AddHash(ref hash, runtime.Outcome);
        AddHash(ref hash, runtime.FailureReason);
        AddHash(ref hash, runtime.StatusHint);
    }

    private static void AddTargetsHash(ref int hash, List<ContractTargetServerData>? targets)
    {
        if (targets == null)
        {
            AddHash(ref hash, 0);
            return;
        }

        AddHash(ref hash, targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            AddHash(ref hash, target.TargetItem);
            AddHash(ref hash, target.Required);
            AddHash(ref hash, target.Progress);
            AddHash(ref hash, target.MatchMode);
        }
    }

    private static void AddRewardsHash(ref int hash, List<ContractRewardData>? rewards)
    {
        if (rewards == null)
        {
            AddHash(ref hash, 0);
            return;
        }

        AddHash(ref hash, rewards.Count);
        for (var i = 0; i < rewards.Count; i++)
        {
            var reward = rewards[i];
            AddHash(ref hash, reward.Type);
            AddHash(ref hash, reward.Id);
            AddHash(ref hash, reward.Amount);
        }
    }

    private static void AddHash<T>(ref int hash, T value)
    {
        unchecked
        {
            hash = hash * 31 + EqualityComparer<T>.Default.GetHashCode(value!);
        }
    }

    private void PopulateDynamicContractSkip(
        EntityUid store,
        NcStoreComponent comp,
        bool hasContractsTab,
        DynamicStateBuffer buf)
    {
        if (!hasContractsTab || !_contracts.TryGetContractSkipInfo(store, comp, out var skipCurrency, out var skipCost))
            return;

        buf.ContractSkipCost = skipCost;
        buf.ContractSkipCurrency = skipCurrency;
    }

    private static int CompareContractsForUi(ContractClientData left, ContractClientData right)
    {
        var poolOrder = left.OfferPoolOrder.CompareTo(right.OfferPoolOrder);
        if (poolOrder != 0)
            return poolOrder;

        var name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        if (name != 0)
            return name;

        return string.CompareOrdinal(left.Id, right.Id);
    }
}
