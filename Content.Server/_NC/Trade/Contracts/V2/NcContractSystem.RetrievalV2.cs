using System;
using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateRetrievalContractData(EntityUid store, NcRetrievalContractPrototype proto)
    {
        var targets = BuildRetrievalTargets(store, proto);
        var totalRequired = CalculateTotalRequired(targets);
        var mainTarget = GetPrimaryTargetId(targets);
        var matchMode = targets.Count > 0 ? targets[0].MatchMode : PrototypeMatchMode.Exact;
        var rewards = BakeRewardsForContract(store, proto.ID, BuildRetrievalRewardDefs(store, proto));

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
            Config = CreateRetrievalObjectiveConfig(proto),
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

    private static ContractObjectiveConfigData CreateRetrievalObjectiveConfig(NcRetrievalContractPrototype proto)
    {
        var config = new ContractObjectiveConfigData();
        var spawn = proto.Spawn;

        if (spawn is { Enabled: true })
        {
            config.RetrievalSpawnEnabled = true;
            config.RetrievalSpawnPoint = CloneContractPointSelector(spawn.Point);
            config.RetrievalSpawnFallbackToStore = spawn.FallbackToStore;
            NormalizeObjectiveConfig(config);
        }

        return config;
    }

    private List<ContractTargetServerData> BuildRetrievalTargets(EntityUid store, NcRetrievalContractPrototype proto)
    {
        if (proto.Targets.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has no targets. " +
                "Use 'targets' with at least one entry.");
            return new();
        }

        var selected = ResolveRetrievalTargetEntries(store, proto);
        var targets = new List<ContractTargetServerData>(selected.Count);

        for (var i = 0; i < selected.Count; i++)
        {
            var (targetIndex, entry) = selected[i];
            if (!TryBuildRetrievalTarget(store, proto.ID, targetIndex, entry, out var target))
                continue;

            targets.Add(target);
        }

        return targets;
    }

    private List<(int Index, NcSupplyTargetEntry Entry)> ResolveRetrievalTargetEntries(
        EntityUid store,
        NcRetrievalContractPrototype proto)
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
            new(QuasiKeyKind.Tc, store, proto.ID, "retrieval-v2"),
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

    private bool TryBuildRetrievalTarget(
        EntityUid store,
        string contractId,
        int index,
        NcSupplyTargetEntry entry,
        out ContractTargetServerData target)
    {
        target = default!;

        if (!TryValidateRetrievalTarget(contractId, index, entry))
            return false;

        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);

        var required = RollFair(
            new(QuasiKeyKind.Req, store, contractId, $"retrieval-target:{index}:{entry.Prototype}:{entry.Group}"),
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

    private List<ContractRewardDef> BuildRetrievalRewardDefs(EntityUid store, NcRetrievalContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
            TryAppendRetrievalRewardEntry(store, proto.ID, $"reward[{i}]", proto.Reward[i], rewards);

        return rewards;
    }

    private bool TryAppendRetrievalRewardEntry(
        EntityUid store,
        string contractId,
        string path,
        NcSupplyRewardEntry entry,
        List<ContractRewardDef> output)
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval contract '{contractId}' {path} references missing entity prototype '{entry.Prototype}'.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Item,
                    Prototype = entry.Prototype,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Currency:
                if (string.IsNullOrWhiteSpace(entry.Currency))
                {
                    Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} is Currency but has no currency.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Currency,
                    Currency = entry.Currency,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Pool:
                if (string.IsNullOrWhiteSpace(entry.Pool))
                {
                    Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} is Pool but has no pool id.");
                    return false;
                }

                if (!_prototypes.HasIndex<NcSupplyRewardPoolPrototype>(entry.Pool))
                {
                    Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} references missing Supply V2 reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Pool,
                    Pool = entry.Pool,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }
}
