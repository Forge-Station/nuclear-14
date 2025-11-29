using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed class NcContractSystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("nccontracts");

    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public void InitContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        // Уже есть контракты — не трогаем (на будущее)
        if (comp.Contracts.Count > 0)
            return;

        if (!TryGetPreset(uid, comp, out var preset))
            return;

        comp.Contracts.Clear();

        // preset здесь формально nullable, но после if мы знаем, что он не null
        foreach (var (key, entry) in preset!.Contracts)
        {
            var id = entry.Id ?? key;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (comp.Contracts.ContainsKey(id))
                continue;

            var data = CreateContractData(id, entry);
            comp.Contracts[id] = data;
        }

        Sawmill.Debug(
            $"[Init] Loaded {comp.Contracts.Count} contracts for {ToPrettyString(uid)} (preset={comp.ContractsPreset}).");
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
            Sawmill.Warning($"[Claim] Contract '{contractId}' not found on {ToPrettyString(store)}.");
            return false;
        }

        // Если контракт не требует предметов – считаем его некорректным
        if (string.IsNullOrWhiteSpace(contract.TargetItem) || contract.Required <= 0)
        {
            Sawmill.Warning(
                $"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has invalid TargetItem/Required.");
            return false;
        }

        // --- ищем тащимый закрытый ящик, как в массовой продаже ---
        EntityUid? crate = null;
        if (TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulled &&
            TryComp(pulled, out EntityStorageComponent? storage) &&
            !storage.Open)
            crate = pulled;

        // --- считаем, сколько всего предметов есть у игрока + в ящике ---
        var ownedUser = _logic.GetOwned(user, contract.TargetItem);
        var ownedCrate = crate is { } crateUid
            ? _logic.GetOwnedInRoot(crateUid, contract.TargetItem)
            : 0;

        var totalOwned = ownedUser + ownedCrate;

        if (totalOwned < contract.Required)
        {
            Sawmill.Debug(
                $"[Claim] Not enough items for contract '{contractId}' on {ToPrettyString(store)}. " +
                $"Have={totalOwned}, Required={contract.Required}.");
            return false; // кто-то успел забрать часть вещей до сдачи
        }

        // Обновим прогресс на всякий случай (на сервере, независимо от UI)
        contract.Progress = Math.Min(totalOwned, contract.Required);

        // --- снимаем сперва с игрока, потом с ящика ---
        var left = contract.Required;

        var takeFromUser = Math.Min(left, ownedUser);
        if (takeFromUser > 0 &&
            !_logic.TryTakeProductUnits(user, contract.TargetItem, takeFromUser))
        {
            Sawmill.Error(
                $"[Claim] Failed to take {takeFromUser}x {contract.TargetItem} from user {ToPrettyString(user)}.");
            return false;
        }

        left -= takeFromUser;

        if (left > 0 && crate is { } crateUid2)
        {
            if (!_logic.TryTakeProductUnitsFromRoot(crateUid2, contract.TargetItem, left))
            {
                Sawmill.Error(
                    $"[Claim] Failed to take {left}x {contract.TargetItem} from crate {ToPrettyString(crateUid2)}.");
                return false;
            }
        }

        // 2) Денежная награда — через общую логику магазина
        if (contract.Reward > 0 && !string.IsNullOrWhiteSpace(contract.RewardCurrency))
        {
            Sawmill.Debug(
                $"[Claim] Reward currency {contract.Reward}x {contract.RewardCurrency} to {ToPrettyString(user)}.");
            _logic.GiveCurrency(user, contract.RewardCurrency, contract.Reward);
        }

        // 3) Предметная награда — тоже через логику магазина
        if (!string.IsNullOrWhiteSpace(contract.RewardItem) && contract.RewardItemCount > 0)
        {
            Sawmill.Debug(
                $"[Claim] Reward items {contract.RewardItemCount}x {contract.RewardItem} to {ToPrettyString(user)}.");
            for (var i = 0; i < contract.RewardItemCount; i++)
                _logic.TrySpawnProduct(contract.RewardItem, user);
        }

        // 4) удаляем контракт и подсовываем новый
        comp.Contracts.Remove(contractId);
        RefillContractsForStore(store, comp);

        return true;
    }

    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (!TryGetPreset(uid, comp, out var preset))
            return;

        // Пробуем добавить ОДИН новый контракт, который ещё не висит у автомата
        foreach (var (key, entry) in preset!.Contracts)
        {
            var id = entry.Id ?? key;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (comp.Contracts.ContainsKey(id))
                continue; // такой уже есть

            var data = CreateContractData(id, entry);
            comp.Contracts[id] = data;

            Sawmill.Info($"[Refill] Added new contract '{id}' to {ToPrettyString(uid)} after claim.");
            break;
        }
    }

    private bool TryGetPreset(EntityUid uid, NcStoreComponent comp, out StoreContractsPresetPrototype? preset)
    {
        preset = null;

        if (string.IsNullOrWhiteSpace(comp.ContractsPreset))
            return false;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(comp.ContractsPreset, out var proto))
        {
            Sawmill.Warning($"[Preset] Preset '{comp.ContractsPreset}' not found for {ToPrettyString(uid)}.");
            return false;
        }

        preset = proto;
        return true;
    }

    private static ContractServerData CreateContractData(
        string id,
        StoreContractsPresetPrototype.ContractPresetEntry entry
    ) =>
        new()
        {
            Id = id,
            TargetItem = entry.TargetItem,
            Required = entry.Required,
            Progress = 0,
            Reward = entry.Reward,
            RewardCurrency = entry.RewardCurrency,
            RewardItem = entry.RewardItem,
            RewardItemCount = entry.RewardItemCount,
            Difficulty = entry.Difficulty,
            Description = entry.Description
        };
}
