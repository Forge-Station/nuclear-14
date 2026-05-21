using Content.Shared._NC.Trade;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Prototypes;
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
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = ContractObjectiveType.Delivery,
            ExecutionKind = ContractExecutionKind.InventoryDelivery,
            Runtime = new(),
            Config = CreateSupplyObjectiveConfig(proto),
            Conditions = CloneContractConditions(proto.Conditions),
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

    private ContractObjectiveConfigData CreateSupplyObjectiveConfig(NcSupplyContractPrototype proto) =>
        new()
        {
            AllowStoreWorldTurnIn = ShouldAllowSupplyStoreWorldTurnIn(proto)
        };

    private bool ShouldAllowSupplyStoreWorldTurnIn(NcSupplyContractPrototype proto)
    {
        for (var i = 0; i < proto.Targets.Count; i++)
        {
            var target = proto.Targets[i];

            if (!string.IsNullOrWhiteSpace(target.Prototype) &&
                IsSupplyWorldTurnInPrototype(target.Prototype))
                return true;

            if (!string.IsNullOrWhiteSpace(target.TagTarget) && IsSupplyWorldTurnInTagTarget(target.TagTarget))
                return true;

            if (string.IsNullOrWhiteSpace(target.Group) ||
                !_prototypes.TryIndex<NcItemGroupPrototype>(target.Group, out var group))
                continue;

            for (var j = 0; j < group.Prototypes.Count; j++)
                if (IsSupplyWorldTurnInPrototype(group.Prototypes[j]))
                    return true;
        }

        return false;
    }

    private bool IsSupplyWorldTurnInPrototype(string prototypeId) =>
        _prototypes.TryIndex<EntityPrototype>(prototypeId, out var proto) &&
        proto.HasComponent<MobStateComponent>(_compFactory) &&
        proto.HasComponent<PullableComponent>(_compFactory);

    private bool IsSupplyWorldTurnInTagTarget(string tagTargetId)
    {
        if (!TryResolveContractTagTargetId(tagTargetId, out var tagId))
            return false;

        foreach (var proto in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (!ContractPrototypeHasTag(proto.ID, tagId))
                continue;

            if (IsSupplyWorldTurnInPrototype(proto.ID))
                return true;
        }

        return false;
    }

    private List<ContractTargetServerData> BuildSupplyTargets(EntityUid store, NcSupplyContractPrototype proto)
    {
        if (proto.Targets.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{proto.ID}' has no targets. " +
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
        NcSupplyContractPrototype proto
    )
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

    private static bool IsSupplyTargetCountConfigured(IntRange targetCount) =>
        targetCount.Min > 0 || targetCount.Max > 0;

    private bool TryBuildSupplyTarget(
        EntityUid store,
        string contractId,
        int index,
        NcSupplyTargetEntry entry,
        out ContractTargetServerData target
    )
    {
        target = default!;

        if (!TryValidateSupplyTarget(contractId, index, entry))
            return false;

        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
        var hasTagTarget = !string.IsNullOrWhiteSpace(entry.TagTarget);

        var required = RollFair(
            new(
                QuasiKeyKind.Req,
                store,
                contractId,
                $"supply-target:{index}:{entry.Prototype}:{entry.Group}:{entry.TagTarget}"),
            entry.Count,
            1);

        if (required <= 0)
            return false;

        if (hasPrototype)
        {
            target = new()
            {
                TargetItem = entry.Prototype,
                Required = required,
                Progress = 0,
                MatchMode = PrototypeMatchMode.Exact
            };
            return true;
        }

        if (hasTagTarget)
        {
            target = new()
            {
                TargetItem = entry.TagTarget,
                Required = required,
                Progress = 0,
                MatchMode = PrototypeMatchMode.Tag
            };
            return true;
        }

        target = new()
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
        List<ContractRewardDef> output
    )
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] Supply contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[Contracts] Supply contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                {
                    Sawmill.Warning(
                        $"[Contracts] Supply contract '{contractId}' {path} references missing entity prototype '{entry.Prototype}'.");
                    return false;
                }

                output.Add(
                    new()
                    {
                        Type = StoreRewardType.Item,
                        RewardId = entry.Prototype,
                        Count = entry.Count,
                        Weight = 1
                    });
                return true;

            case StoreRewardType.Currency:
                if (string.IsNullOrWhiteSpace(entry.Currency))
                {
                    Sawmill.Warning(
                        $"[Contracts] Supply contract '{contractId}' {path} is Currency but has no currency.");
                    return false;
                }

                output.Add(
                    new()
                    {
                        Type = StoreRewardType.Currency,
                        RewardId = entry.Currency,
                        Count = entry.Count,
                        Weight = 1
                    });
                return true;

            case StoreRewardType.Pool:
                if (string.IsNullOrWhiteSpace(entry.Pool))
                {
                    Sawmill.Warning($"[Contracts] Supply contract '{contractId}' {path} is Pool but has no pool id.");
                    return false;
                }

                if (!_prototypes.HasIndex<NcSupplyRewardPoolPrototype>(entry.Pool))
                {
                    Sawmill.Warning(
                        $"[Contracts] Supply contract '{contractId}' {path} references missing Supply reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                output.Add(
                    new()
                    {
                        Type = StoreRewardType.Pool,
                        RewardId = entry.Pool,
                        Count = entry.Count,
                        Weight = 1
                    });
                return true;

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[Contracts] Supply contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning(
                    $"[Contracts] Supply contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private static bool IsStrictPositiveRange(IntRange range) =>
        range.Min > 0 && range.Max > 0 && range.Min <= range.Max;
}
