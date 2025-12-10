using System.Linq;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed class NcContractSystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("nccontracts");

    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public void InitContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (comp.Contracts.Count > 0)
            return;

        if (!TryGetPreset(uid, comp, out var preset))
            return;

        AddMissingContractsFromPreset(uid, comp, preset!, false);
    }

    private static List<ContractTargetServerData> GetEffectiveTargets(ContractServerData contract)
    {
        if (contract.Targets.Count > 0)
            return contract.Targets;

        if (!string.IsNullOrWhiteSpace(contract.TargetItem) && contract.Required > 0)
        {
            return new()
            {
                new()
                {
                    TargetItem = contract.TargetItem,
                    Required = contract.Required,
                    Progress = contract.Progress
                }
            };
        }

        return new();
    }

    public bool TryClaim(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
        {
            Sawmill.Warning($"[Claim] Store {ToPrettyString(store)} has no NcStoreComponent.");
            return false;
        }

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
        {
            Sawmill.Warning($"[Claim] Store {ToPrettyString(store)} has no contract '{contractId}'.");
            return false;
        }

        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
        {
            Sawmill.Warning(
                $"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has no valid targets.");
            return false;
        }

        var crate = GetPulledClosedCrate(user);

        // 1) Проверяем, что по КАЖДОЙ цели хватает предметов.
        foreach (var t in targets)
        {
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
            {
                Sawmill.Warning(
                    $"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has invalid target '{t.TargetItem}'.");
                return false;
            }

            var ownedUser = _logic.GetOwned(user, t.TargetItem);
            var ownedCrate = crate is { } crateUid
                ? _logic.GetOwnedInRoot(crateUid, t.TargetItem)
                : 0;

            var totalOwned = ownedUser + ownedCrate;
            if (totalOwned < t.Required)
                return false;
        }

        // 2) Реально забираем предметы по всем целям.
        foreach (var t in targets)
        {
            var left = t.Required;

            var ownedUser = _logic.GetOwned(user, t.TargetItem);
            var takeFromUser = Math.Min(left, ownedUser);

            if (takeFromUser > 0 &&
                !_logic.TryTakeProductUnits(user, t.TargetItem, takeFromUser))
            {
                Sawmill.Error(
                    $"[Claim] Failed to take {takeFromUser}x {t.TargetItem} " +
                    $"from user {ToPrettyString(user)} for contract '{contractId}' on {ToPrettyString(store)}.");
                return false;
            }

            left -= takeFromUser;

            if (left > 0 && crate is { } crateUid2)
            {
                if (!_logic.TryTakeProductUnitsFromRoot(crateUid2, t.TargetItem, left))
                {
                    Sawmill.Error(
                        $"[Claim] Failed to take {left}x {t.TargetItem} " +
                        $"from crate {ToPrettyString(crateUid2)} for contract '{contractId}' on {ToPrettyString(store)}.");
                    return false;
                }
            }

            t.Progress = t.Required;
        }

        if (contract.Targets.Count > 0)
        {
            var totalRequired = 0;
            var totalProgress = 0;

            foreach (var t in contract.Targets)
            {
                if (t.Required <= 0 || string.IsNullOrWhiteSpace(t.TargetItem))
                    continue;

                totalRequired += t.Required;

                var prog = t.Progress;
                if (prog < 0)
                    prog = 0;
                if (prog > t.Required)
                    prog = t.Required;

                totalProgress += prog;
            }

            contract.Required = totalRequired;
            contract.Progress = totalProgress;

            if (contract.Targets.Count > 0)
                contract.TargetItem = contract.Targets[0].TargetItem;
        }


        if (contract.RewardCurrencies is { Count: > 0, })
        {
            foreach (var kvp in contract.RewardCurrencies)
            {
                var currencyId = kvp.Key;
                var amount = kvp.Value;

                if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
                    continue;

                _logic.GiveCurrency(user, currencyId, amount);
            }
        }
        else if (contract.Reward > 0 && !string.IsNullOrWhiteSpace(contract.RewardCurrency))
            _logic.GiveCurrency(user, contract.RewardCurrency, contract.Reward);

        if (contract.RewardItems is { Count: > 0, })
        {
            foreach (var kvp in contract.RewardItems)
            {
                var protoId = kvp.Key;
                var count = kvp.Value;

                if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                    continue;

                for (var i = 0; i < count; i++)
                    _logic.TrySpawnProduct(protoId, user);
            }
        }
        else if (!string.IsNullOrWhiteSpace(contract.RewardItem) &&
            contract.RewardItemCount > 0)
        {
            for (var i = 0; i < contract.RewardItemCount; i++)
                _logic.TrySpawnProduct(contract.RewardItem!, user);
        }

        comp.Contracts.Remove(contractId);
        RefillContractsForStore(store, comp);

        return true;
    }


    private EntityUid? GetPulledClosedCrate(EntityUid user)
    {
        if (!TryComp(user, out PullerComponent? puller))
            return null;

        if (puller.Pulling is not { } pulled)
            return null;

        if (!TryComp(pulled, out EntityStorageComponent? storage))
            return null;

        return storage.Open ? null : pulled;
    }

    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (!TryGetPreset(uid, comp, out var preset))
            return;

        AddMissingContractsFromPreset(uid, comp, preset!, true);
    }

    private void AddMissingContractsFromPreset(
        EntityUid uid,
        NcStoreComponent comp,
        StoreContractsPresetPrototype preset,
        bool fillOnlyFirstMissing
    )
    {
        foreach (var contractId in preset.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contractId))
                continue;

            if (comp.Contracts.ContainsKey(contractId))
                continue;

            if (!_prototypes.TryIndex<StoreContractPrototype>(contractId, out var proto))
            {
                Sawmill.Warning(
                    $"[Contracts] Contract '{contractId}' from preset '{preset.ID}' not found for {ToPrettyString(uid)}.");
                continue;
            }

            comp.Contracts[contractId] = CreateContractData(proto);

            if (fillOnlyFirstMissing)
                break;
        }
    }

    private bool TryGetPreset(
        EntityUid uid,
        NcStoreComponent comp,
        out StoreContractsPresetPrototype? preset
    )
    {
        preset = null;

        string? presetId = null;

        if (comp.ContractPresets.Count > 0)
            presetId = comp.ContractPresets[0];
        else if (!string.IsNullOrWhiteSpace(comp.LegacyContractsPreset))
            presetId = comp.LegacyContractsPreset;

        if (string.IsNullOrWhiteSpace(presetId))
            return false;

        if (!_prototypes.TryIndex(presetId, out preset))
        {
            Sawmill.Warning(
                $"[Preset] Preset '{presetId}' not found for {ToPrettyString(uid)}.");
            return false;
        }

        return true;
    }

    private static int GetRandomSteppedAmount(IRobustRandom random, int min, int max)
    {
        if (max < min)
            (min, max) = (max, min);

        min = Math.Max(min, 0);
        max = Math.Max(max, 0);

        if (max <= min)
            return min;

        static int TryPick(IRobustRandom random, int min, int max, int step)
        {
            if (step <= 0)
                return -1;

            var minStep = (min + step - 1) / step * step;
            var maxStep = max / step * step;

            if (maxStep < minStep)
                return -1;

            var steps = (maxStep - minStep) / step + 1;
            var idx = random.Next(steps);
            return minStep + idx * step;
        }

        var val = TryPick(random, min, max, 5);
        if (val >= 0)
            return val;

        val = TryPick(random, min, max, 3);
        if (val >= 0)
            return val;
        return min;
    }

    private static void ApplyBonusReward(
        StoreContractBonusReward bonus,
        Dictionary<string, int> rewardCurrencies,
        Dictionary<string, int> rewardItems,
        bool ignoreReplace = false
    )
    {
        if (!ignoreReplace && bonus.Mode == StoreContractBonusMode.Replace)
        {
            rewardCurrencies.Clear();
            rewardItems.Clear();
        }

        if (!string.IsNullOrWhiteSpace(bonus.Id))
        {
            var addCount = bonus.Count > 0 ? bonus.Count : 1;

            if (rewardItems.TryGetValue(bonus.Id, out var existing))
                rewardItems[bonus.Id] = existing + addCount;
            else
                rewardItems[bonus.Id] = addCount;
        }

        if (bonus.RewardCurrencies != null)
        {
            foreach (var kvp in bonus.RewardCurrencies)
            {
                var currencyId = kvp.Key;
                var amount = kvp.Value;

                if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
                    continue;

                if (rewardCurrencies.TryGetValue(currencyId, out var existing))
                    rewardCurrencies[currencyId] = existing + amount;
                else
                    rewardCurrencies[currencyId] = amount;
            }
        }

        if (bonus.RewardItems != null)
        {
            foreach (var kvp in bonus.RewardItems)
            {
                var protoId = kvp.Key;
                var count = kvp.Value;

                if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                    continue;

                if (rewardItems.TryGetValue(protoId, out var existing))
                    rewardItems[protoId] = existing + count;
                else
                    rewardItems[protoId] = count;
            }
        }
    }


    private ContractServerData CreateContractData(StoreContractPrototype proto)
    {
        // 1. Цели контракта
        var targets = new List<ContractTargetServerData>();

        var targetItem = proto.TargetItem ?? string.Empty;
        var required = proto.Required;

        if (proto.Targets is { Count: > 0, })
        {
            var targetCount = proto.TargetCount;
            if (targetCount <= 0)
                targetCount = 1;

            if (targetCount == 1)
            {
                var chosen = PickWeighted(_random, proto.Targets, t => t.Weight);
                if (chosen != null)
                {
                    targetItem = chosen.TargetItemId ?? targetItem;

                    if (chosen.Required > 0)
                        required = chosen.Required;
                }
            }
            else
            {
                var pool = proto.Targets.ToList();
                var picks = Math.Min(targetCount, pool.Count);

                for (var i = 0; i < picks && pool.Count > 0; i++)
                {
                    var chosen = PickWeighted(_random, pool, t => t.Weight);
                    if (chosen == null)
                        break;

                    pool.Remove(chosen);

                    var itemId = chosen.TargetItemId ?? targetItem;
                    var req = chosen.Required > 0 ? chosen.Required : required;

                    targets.Add(
                        new()
                        {
                            TargetItem = itemId,
                            Required = req,
                            Progress = 0
                        });
                }

                if (targets.Count > 0)
                {
                    targetItem = targets[0].TargetItem;
                    required = 0;
                    foreach (var t in targets)
                        required += t.Required;
                }
            }
        }

        // 2. Накопление базовой награды
        var rewardCurrencies = new Dictionary<string, int>();
        var rewardItems = new Dictionary<string, int>();

        // 2.1 Диапазоны валют (Currencies)
        if (proto.Currencies is { Count: > 0, })
        {
            foreach (var c in proto.Currencies)
            {
                if (string.IsNullOrWhiteSpace(c.Id))
                    continue;

                var min = c.Min;
                var max = c.Max;

                if (max < min)
                    (min, max) = (max, min);

                min = Math.Max(min, 0);
                if (max < 0)
                    continue;

                var amount = GetRandomSteppedAmount(_random, min, max);
                if (amount <= 0)
                    continue;

                if (rewardCurrencies.TryGetValue(c.Id, out var existing))
                    rewardCurrencies[c.Id] = existing + amount;
                else
                    rewardCurrencies[c.Id] = amount;
            }
        }

        // 2.2 Фикс. словарь валют (RewardCurrencies)
        if (proto.RewardCurrencies != null)
        {
            foreach (var kvp in proto.RewardCurrencies)
            {
                var currencyId = kvp.Key;
                var amount = kvp.Value;

                if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
                    continue;

                if (rewardCurrencies.TryGetValue(currencyId, out var existing))
                    rewardCurrencies[currencyId] = existing + amount;
                else
                    rewardCurrencies[currencyId] = amount;
            }
        }

        // 2.3 Фикс. словарь предметов (RewardItems)
        if (proto.RewardItems != null)
        {
            foreach (var kvp in proto.RewardItems)
            {
                var protoId = kvp.Key;
                var count = kvp.Value;

                if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                    continue;

                if (rewardItems.TryGetValue(protoId, out var existing))
                    rewardItems[protoId] = existing + count;
                else
                    rewardItems[protoId] = count;
            }
        }

        // 2.4 Старые поля Reward / RewardCurrency
        if (proto.Reward > 0 && !string.IsNullOrWhiteSpace(proto.RewardCurrency))
        {
            var currencyId = proto.RewardCurrency;
            var amount = proto.Reward;

            if (rewardCurrencies.TryGetValue(currencyId, out var existing))
                rewardCurrencies[currencyId] = existing + amount;
            else
                rewardCurrencies[currencyId] = amount;
        }

        // 2.5 Старые поля RewardItem / RewardItemCount
        if (!string.IsNullOrWhiteSpace(proto.RewardItem) &&
            proto.RewardItemCount > 0)
        {
            var protoId = proto.RewardItem!;
            var count = proto.RewardItemCount;

            if (rewardItems.TryGetValue(protoId, out var existing))
                rewardItems[protoId] = existing + count;
            else
                rewardItems[protoId] = count;
        }

        var allBonusRewards = new List<StoreContractBonusReward>();

        if (proto.BonusRewards is { Count: > 0, })
            allBonusRewards.AddRange(proto.BonusRewards);

        if (proto.RewardPools is { Count: > 0, })
        {
            foreach (var poolRef in proto.RewardPools)
            {
                if (!_prototypes.TryIndex<NcContractRewardPoolPrototype>(poolRef.ID, out var poolProto))
                {
                    Sawmill.Error($"[Contracts] RewardPool '{poolRef.ID}' not found for contract '{proto.ID}'.");
                    continue;
                }

                if (poolProto.Entries is not { Count: > 0, })
                    continue;

                var poolWeight = poolRef.Weight <= 0 ? 1 : poolRef.Weight;

                foreach (var entry in poolProto.Entries)
                {
                    if (entry == null)
                        continue;

                    // Копируем entry, чтобы не мутировать прототип
                    var entryWeight = entry.Weight <= 0 ? 1 : entry.Weight;
                    var combinedWeight = entryWeight * poolWeight;

                    var copy = new StoreContractBonusReward
                    {
                        Id = entry.Id,
                        Count = entry.Count,
                        Weight = combinedWeight,
                        Mode = entry.Mode,
                        RewardCurrencies = entry.RewardCurrencies != null
                            ? new Dictionary<string, int>(entry.RewardCurrencies)
                            : null,
                        RewardItems = entry.RewardItems != null
                            ? new Dictionary<string, int>(entry.RewardItems)
                            : null
                    };

                    allBonusRewards.Add(copy);
                }
            }
        }

        if (allBonusRewards.Count > 0 && proto.BonusPickCount > 0)
        {
            var pool = allBonusRewards.ToList();
            var picks = Math.Min(proto.BonusPickCount, pool.Count);

            var isFirst = true;

            for (var i = 0; i < picks && pool.Count > 0; i++)
            {
                var bonus = PickWeighted(_random, pool, b => b.Weight);
                if (bonus == null)
                    break;

                pool.Remove(bonus);

                // для первого учитываем Replace, дальше только Add
                ApplyBonusReward(bonus, rewardCurrencies, rewardItems, !isFirst);
                isFirst = false;
            }
        }

        // 4. Выбираем «основную» валюту и «основной» предмет
        string? mainCurrency = null;
        var mainCurrencyAmount = 0;

        foreach (var kvp in rewardCurrencies)
        {
            var currencyId = kvp.Key;
            var amount = kvp.Value;

            if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
                continue;

            if (amount > mainCurrencyAmount)
            {
                mainCurrency = currencyId;
                mainCurrencyAmount = amount;
            }
        }

        string? mainItem = null;
        var mainItemCount = 0;

        foreach (var kvp in rewardItems)
        {
            var protoId = kvp.Key;
            var count = kvp.Value;

            if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                continue;

            if (count > mainItemCount)
            {
                mainItem = protoId;
                mainItemCount = count;
            }
        }

        // 5. Финальная сборка контракта
        return new()
        {
            Id = proto.ID,
            Name = proto.Name,
            TargetItem = targetItem,
            Required = required,
            Progress = 0,

            Reward = mainCurrencyAmount,
            RewardCurrency = mainCurrency ?? string.Empty,
            RewardItem = mainItem,
            RewardItemCount = mainItemCount,

            Difficulty = proto.Difficulty,
            Description = proto.Description,

            RewardCurrencies = rewardCurrencies,
            RewardItems = rewardItems,
            Targets = targets
        };
    }


    private static T? PickWeighted<T>(
        IRobustRandom random,
        IReadOnlyList<T> list,
        Func<T, int> weightSelector
    )
        where T : class
    {
        if (list.Count == 0)
            return null;

        var total = 0;
        var weights = new int[list.Count];

        for (var i = 0; i < list.Count; i++)
        {
            var w = weightSelector(list[i]);
            if (w <= 0)
                w = 1;

            weights[i] = w;
            total += w;
        }

        if (total <= 0)
            return list[random.Next(list.Count)];

        var value = random.Next(total);
        var accum = 0;

        for (var i = 0; i < list.Count; i++)
        {
            accum += weights[i];
            if (value < accum)
                return list[i];
        }

        return list[^1];
    }
}
