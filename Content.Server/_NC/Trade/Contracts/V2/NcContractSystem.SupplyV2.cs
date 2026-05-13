using System;
using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateSupplyContractData(EntityUid store, NcSupplyContractPrototype proto)
    {
        var targets = BuildSupplyTargets(store, proto);
        var totalRequired = CalculateTotalRequired(targets);
        var mainTarget = GetPrimaryTargetId(targets);
        var matchMode = targets.Count > 0 ? targets[0].MatchMode : PrototypeMatchMode.Exact;
        var rewards = BakeRewardsForContract(store, proto.ID, BuildSupplyRewardDefs(store, proto));

        var contract = new ContractServerData
        {
            Id = proto.ID,
            Name = proto.Name,
            Difficulty = proto.Difficulty,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = ContractObjectiveType.Delivery,
            Runtime = new ContractRuntimeContextData(),
            Config = new ContractObjectiveConfigData(),
            FlowStatus = ContractFlowStatus.Available,
            MatchMode = matchMode,
            Targets = targets,
            TargetItem = mainTarget,
            Required = totalRequired,
            Progress = 0,
            Rewards = rewards
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private List<ContractTargetServerData> BuildSupplyTargets(EntityUid store, NcSupplyContractPrototype proto)
    {
        if (proto.Targets.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has no targets. " +
                "Use 'targets' with at least one entry.");
            return new();
        }

        var selected = ResolveSupplyTargetEntries(store, proto);
        var targets = new List<ContractTargetServerData>(selected.Count);

        for (var i = 0; i < selected.Count; i++)
        {
            var (targetIndex, entry) = selected[i];
            if (!TryBuildSupplyTarget(store, proto.ID, targetIndex, entry, out var target))
                continue;

            targets.Add(target);
        }

        return targets;
    }

    private List<(int Index, NcSupplyTargetEntry Entry)> ResolveSupplyTargetEntries(
        EntityUid store,
        NcSupplyContractPrototype proto)
    {
        var result = new List<(int Index, NcSupplyTargetEntry Entry)>();
        if (proto.Targets.Count == 0)
            return result;

        if (!IsSupplyTargetCountConfigured(proto.TargetCount))
        {
            result.Capacity = proto.Targets.Count;
            for (var i = 0; i < proto.Targets.Count; i++)
                result.Add((i, proto.Targets[i]));

            return result;
        }

        var targetCount = RollFair(
            new(QuasiKeyKind.Tc, store, proto.ID, "supply-v2"),
            proto.TargetCount,
            1,
            proto.Targets.Count);

        var picks = Math.Clamp(targetCount, 1, proto.Targets.Count);
        var pool = new List<int>(proto.Targets.Count);
        for (var i = 0; i < proto.Targets.Count; i++)
            pool.Add(i);

        result.Capacity = picks;
        for (var i = 0; i < picks && pool.Count > 0; i++)
        {
            var chosenIndex = PickWeighted(_random, pool, index => Math.Max(0, proto.Targets[index].Weight));
            pool.Remove(chosenIndex);
            result.Add((chosenIndex, proto.Targets[chosenIndex]));
        }

        return result;
    }

    private static bool IsSupplyTargetCountConfigured(IntRange targetCount)
    {
        return targetCount.Min > 0 || targetCount.Max > 0;
    }

    private bool TryBuildSupplyTarget(
        EntityUid store,
        string contractId,
        int index,
        NcSupplyTargetEntry entry,
        out ContractTargetServerData target)
    {
        target = default!;

        if (!TryValidateSupplyTarget(contractId, index, entry))
            return false;

        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);

        var required = RollFair(
            new(QuasiKeyKind.Req, store, contractId, $"supply-target:{index}:{entry.Prototype}:{entry.Group}"),
            entry.Count,
            1);

        if (required <= 0)
            return false;

        if (hasPrototype)
        {
            target = new ContractTargetServerData
            {
                TargetItem = entry.Prototype,
                Required = required,
                Progress = 0,
                MatchMode = PrototypeMatchMode.Exact
            };
            return true;
        }

        target = new ContractTargetServerData
        {
            TargetItem = entry.Group,
            Required = required,
            Progress = 0,
            MatchMode = PrototypeMatchMode.Matcher
        };
        return true;
    }

    private List<ContractRewardDef> BuildSupplyRewardDefs(EntityUid store, NcSupplyContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
            TryAppendSupplyRewardEntry(store, proto.ID, $"reward[{i}]", proto.Reward[i], rewards);

        return rewards;
    }

    private bool TryAppendSupplyRewardEntry(
        EntityUid store,
        string contractId,
        string path,
        NcSupplyRewardEntry entry,
        List<ContractRewardDef> output)
    {
        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Supply contract '{contractId}' {path} references missing entity prototype '{entry.Prototype}'.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Item,
                    Prototype = entry.Prototype,
                    Amount = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Currency:
                if (string.IsNullOrWhiteSpace(entry.Currency))
                {
                    Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} is Currency but has no currency.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Currency,
                    Currency = entry.Currency,
                    Amount = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Pool:
                if (string.IsNullOrWhiteSpace(entry.Pool))
                {
                    Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} is Pool but has no pool id.");
                    return false;
                }

                if (!_prototypes.HasIndex<NcContractRewardPoolPrototype>(entry.Pool))
                {
                    Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} references missing reward pool '{entry.Pool}'.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Pool,
                    Pool = entry.Pool,
                    Amount = entry.Count,
                    Weight = 1
                });
                return true;

            default:
                Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private static bool IsStrictPositiveRange(IntRange range)
    {
        return range.Min > 0 && range.Max > 0 && range.Min <= range.Max;
    }

}
