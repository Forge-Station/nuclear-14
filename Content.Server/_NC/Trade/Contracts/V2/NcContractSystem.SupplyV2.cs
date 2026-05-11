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

    private IReadOnlyList<NcSupplyRequirementEntry> GetSupplyRequirements(NcSupplyContractPrototype proto)
    {
        if (proto.Requirements.Count > 0)
        {
            if (proto.LegacyRequire.Count > 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{proto.ID}' uses both 'requirements' and legacy 'require'. " +
                    "Only 'requirements' will be used.");
            }

            return proto.Requirements;
        }

        if (proto.LegacyRequire.Count > 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' uses legacy field 'require'. " +
                "Rename it to 'requirements'.");
            return proto.LegacyRequire;
        }

        return Array.Empty<NcSupplyRequirementEntry>();
    }

    private List<ContractTargetServerData> BuildSupplyTargets(EntityUid store, NcSupplyContractPrototype proto)
    {
        var requirements = GetSupplyRequirements(proto);
        if (requirements.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has no requirements. " +
                "Use 'requirements' with at least one entry.");
        }

        var targets = new List<ContractTargetServerData>(requirements.Count);

        for (var i = 0; i < requirements.Count; i++)
        {
            var entry = requirements[i];
            if (!TryBuildSupplyTarget(store, proto.ID, i, entry, out var target))
                continue;

            targets.Add(target);
        }

        return targets;
    }

    private bool TryBuildSupplyTarget(
        EntityUid store,
        string contractId,
        int index,
        NcSupplyRequirementEntry entry,
        out ContractTargetServerData target)
    {
        target = default!;

        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
        var hasGroup = !string.IsNullOrWhiteSpace(entry.Group);

        if (hasPrototype == hasGroup)
        {
            Sawmill.Warning(
                hasPrototype
                    ? $"[ContractsV2] Supply contract '{contractId}' requirement #{index} has both prototype and group. Use exactly one."
                    : $"[ContractsV2] Supply contract '{contractId}' requirement #{index} has neither prototype nor group.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' requirement #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        var required = RollFair(
            new(QuasiKeyKind.Req, store, contractId, $"supply:{index}:{entry.Prototype}:{entry.Group}"),
            entry.Count,
            1);

        if (required <= 0)
            return false;

        if (hasPrototype)
        {
            if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{contractId}' references missing entity prototype '{entry.Prototype}'.");
                return false;
            }

            target = new ContractTargetServerData
            {
                TargetItem = entry.Prototype,
                Required = required,
                Progress = 0,
                MatchMode = PrototypeMatchMode.Exact
            };
            return true;
        }

        if (!_prototypes.HasIndex<NcItemGroupPrototype>(entry.Group))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' references missing ncItemGroup '{entry.Group}'. " +
                "Supply V2 group requirements must reference ncItemGroup prototypes, not legacy matchers.");
            return false;
        }

        if (!TryGetContractMatcherSpec(entry.Group, out _))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' references invalid item group '{entry.Group}'.");
            return false;
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
        var rewards = new List<ContractRewardDef>();
        var hasRewardsBlock = HasSupplyRewards(proto.Rewards);

        if (hasRewardsBlock)
        {
            AppendSupplyRewardBlock(store, proto, rewards);

            if (proto.LegacyReward.Money > 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{proto.ID}' uses both new 'rewards' block and legacy 'reward.money'. " +
                    "Legacy reward.money is ignored. Put currency rewards under rewards.guaranteed.");
            }

            return rewards;
        }

        AppendLegacySupplyReward(store, proto, rewards);
        return rewards;
    }

    private static bool HasSupplyRewards(Content.Shared._NC.Trade.NcSupplyRewardsData rewards)
    {
        return rewards.Guaranteed.Count > 0 || rewards.Random.Count > 0 || rewards.Pools.Count > 0;
    }

    private void AppendSupplyRewardBlock(
        EntityUid store,
        NcSupplyContractPrototype proto,
        List<ContractRewardDef> output)
    {
        for (var i = 0; i < proto.Rewards.Guaranteed.Count; i++)
            TryAppendSupplyRewardEntry(store, proto.ID, $"rewards.guaranteed[{i}]", proto.Rewards.Guaranteed[i], 1.0f, output);

        for (var i = 0; i < proto.Rewards.Random.Count; i++)
            TryAppendSupplyRewardEntry(store, proto.ID, $"rewards.random[{i}]", proto.Rewards.Random[i], proto.Rewards.Random[i].Chance, output);

        for (var i = 0; i < proto.Rewards.Pools.Count; i++)
            TryAppendSupplyRewardPoolRoll(proto.ID, $"rewards.pools[{i}]", proto.Rewards.Pools[i], output);
    }

    private bool TryAppendSupplyRewardEntry(
        EntityUid store,
        string contractId,
        string path,
        Content.Shared._NC.Trade.NcSupplyRewardEntry entry,
        float chance,
        List<ContractRewardDef> output)
    {
        if (!IsChanceValid(chance))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has invalid chance={chance}. Expected 0..1.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Amount))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' {path} has invalid amount range " +
                $"{entry.Amount.Min}..{entry.Amount.Max}.");
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
                    Amount = entry.Amount,
                    Probability = chance,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Currency:
                var currency = ResolveSupplyRewardCurrency(store, entry.Currency);
                if (string.IsNullOrWhiteSpace(currency))
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Supply contract '{contractId}' {path} is Currency but has no currency " +
                        "and no contracts preset skipCurrency fallback.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Currency,
                    Currency = currency,
                    Amount = entry.Amount,
                    Probability = chance,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Pool:
                Sawmill.Warning(
                    $"[ContractsV2] Supply contract '{contractId}' {path} uses type: Pool inside guaranteed/random. " +
                    "Use rewards.pools instead.");
                return false;

            default:
                Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool TryAppendSupplyRewardPoolRoll(
        string contractId,
        string path,
        Content.Shared._NC.Trade.NcSupplyRewardPoolRollEntry entry,
        List<ContractRewardDef> output)
    {
        if (string.IsNullOrWhiteSpace(entry.Pool))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has no pool id.");
            return false;
        }

        if (!_prototypes.HasIndex<NcContractRewardPoolPrototype>(entry.Pool))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} references missing reward pool '{entry.Pool}'.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Rolls))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{contractId}' {path} has invalid rolls range " +
                $"{entry.Rolls.Min}..{entry.Rolls.Max}.");
            return false;
        }

        if (!IsChanceValid(entry.Chance))
        {
            Sawmill.Warning($"[ContractsV2] Supply contract '{contractId}' {path} has invalid chance={entry.Chance}. Expected 0..1.");
            return false;
        }

        output.Add(new ContractRewardDef
        {
            Type = StoreRewardType.Pool,
            Pool = entry.Pool,
            Amount = entry.Rolls,
            Probability = entry.Chance,
            Weight = 1
        });
        return true;
    }

    private void AppendLegacySupplyReward(EntityUid store, NcSupplyContractPrototype proto, List<ContractRewardDef> rewards)
    {
        var money = proto.LegacyReward.Money;
        if (money <= 0)
            return;

        Sawmill.Warning(
            $"[ContractsV2] Supply contract '{proto.ID}' uses legacy 'reward.money'. " +
            "Prefer rewards.guaranteed with type: Currency.");

        var currency = ResolveSupplyRewardCurrency(store, proto.LegacyReward.Currency);
        if (string.IsNullOrWhiteSpace(currency))
        {
            Sawmill.Warning(
                $"[ContractsV2] Supply contract '{proto.ID}' has reward.money={money}, but no reward.currency " +
                "and no contracts preset skipCurrency fallback. Money reward skipped.");
            return;
        }

        rewards.Add(new ContractRewardDef
        {
            Type = StoreRewardType.Currency,
            Currency = currency,
            Amount = IntRange.Fixed(money),
            Probability = 1.0f,
            Weight = 1
        });
    }

    private static bool IsStrictPositiveRange(IntRange range)
    {
        return range.Min > 0 && range.Max > 0 && range.Min <= range.Max;
    }

    private static bool IsChanceValid(float chance)
    {
        return chance >= 0f && chance <= 1f;
    }

    private string ResolveSupplyRewardCurrency(EntityUid store, string explicitCurrency)
    {
        if (!string.IsNullOrWhiteSpace(explicitCurrency))
            return explicitCurrency;

        if (!TryComp(store, out NcStoreComponent? comp))
            return string.Empty;

        return TryResolveContractPreset(store, comp, out var preset)
            ? preset.SkipCurrency
            : string.Empty;
    }
}
