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

    /// <summary>
    /// Инициализация контрактах на магазине при спавне.
    /// Берёт пресет и генерирует ContractServerData из StoreContractPrototype.
    /// </summary>
    public void InitContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        // Уже есть контракты — не трогаем
        if (comp.Contracts.Count > 0)
            return;

        if (!TryGetPreset(uid, comp, out var preset))
            return;

        comp.Contracts.Clear();

        // preset.Contracts — это List<string> с id storeContract
        foreach (var contractId in preset!.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contractId))
                continue;

            // Уже есть такой контракт — пропускаем
            if (comp.Contracts.ContainsKey(contractId))
                continue;

            if (!_prototypes.TryIndex<StoreContractPrototype>(contractId, out var proto))
            {
                Sawmill.Warning(
                    $"[Init] Contract '{contractId}' from preset '{preset.ID}' not found for {ToPrettyString(uid)}.");
                continue;
            }

            var data = CreateContractData(proto);
            comp.Contracts[contractId] = data;
        }

        Sawmill.Debug(
            $"[Init] Loaded {comp.Contracts.Count} contract(s) for {ToPrettyString(uid)} (preset={preset.ID}).");
    }

    /// <summary>
    /// Попытка сдать контракт и выдать награду.
    /// </summary>
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

        EntityUid? crate = null;
        if (TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulled &&
            TryComp(pulled, out EntityStorageComponent? storage) &&
            !storage.Open)
        {
            crate = pulled;
        }

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
            return false;
        }

        contract.Progress = Math.Min(totalOwned, contract.Required);

        var left = contract.Required;

        // Сначала забираем с игрока
        var takeFromUser = Math.Min(left, ownedUser);
        if (takeFromUser > 0 &&
            !_logic.TryTakeProductUnits(user, contract.TargetItem, takeFromUser))
        {
            Sawmill.Error(
                $"[Claim] Failed to take {takeFromUser}x {contract.TargetItem} from user {ToPrettyString(user)}.");
            return false;
        }

        left -= takeFromUser;

        // Остаток – из ящика
        if (left > 0 && crate is { } crateUid2)
        {
            if (!_logic.TryTakeProductUnitsFromRoot(crateUid2, contract.TargetItem, left))
            {
                Sawmill.Error(
                    $"[Claim] Failed to take {left}x {contract.TargetItem} from crate {ToPrettyString(crateUid2)}.");
                return false;
            }
        }

        // Награда валютой
        if (contract.Reward > 0 && !string.IsNullOrWhiteSpace(contract.RewardCurrency))
        {
            Sawmill.Debug(
                $"[Claim] Reward currency {contract.Reward}x {contract.RewardCurrency} to {ToPrettyString(user)}.");
            _logic.GiveCurrency(user, contract.RewardCurrency, contract.Reward);
        }

        // Награда предметами
        if (!string.IsNullOrWhiteSpace(contract.RewardItem) && contract.RewardItemCount > 0)
        {
            Sawmill.Debug(
                $"[Claim] Reward items {contract.RewardItemCount}x {contract.RewardItem} to {ToPrettyString(user)}.");
            for (var i = 0; i < contract.RewardItemCount; i++)
                _logic.TrySpawnProduct(contract.RewardItem, user);
        }

        // Удаляем контракт и пытаемся добрать новый из пресета
        comp.Contracts.Remove(contractId);
        RefillContractsForStore(store, comp);

        return true;
    }

    /// <summary>
    /// Добираем новый контракт из того же пресета после сдачи.
    /// </summary>
    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (!TryGetPreset(uid, comp, out var preset))
            return;

        foreach (var contractId in preset!.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contractId))
                continue;

            if (comp.Contracts.ContainsKey(contractId))
                continue;

            if (!_prototypes.TryIndex<StoreContractPrototype>(contractId, out var proto))
            {
                Sawmill.Warning(
                    $"[Refill] Contract '{contractId}' from preset '{preset.ID}' not found for {ToPrettyString(uid)}.");
                continue;
            }

            var data = CreateContractData(proto);
            comp.Contracts[contractId] = data;

            Sawmill.Info(
                $"[Refill] Added new contract '{contractId}' to {ToPrettyString(uid)} after claim (preset={preset.ID}).");
            break; // добавили один — выходим
        }
    }

    /// <summary>
    /// Выбираем пресет контрактов для магазина
    /// (новый формат: ContractPresets[], старый: LegacyContractsPreset).
    /// </summary>
    private bool TryGetPreset(EntityUid uid, NcStoreComponent comp, out StoreContractsPresetPrototype? preset)
    {
        preset = null;

        string? presetId = null;

        // Новый путь: несколько пресетов
        if (comp.ContractPresets.Count > 0)
            presetId = comp.ContractPresets[0];
        // Старый путь: одно поле contracts: "preset_id"
        else if (!string.IsNullOrWhiteSpace(comp.LegacyContractsPreset))
            presetId = comp.LegacyContractsPreset;

        if (string.IsNullOrWhiteSpace(presetId))
            return false;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(presetId, out var proto))
        {
            Sawmill.Warning(
                $"[Preset] Preset '{presetId}' not found for {ToPrettyString(uid)}.");
            return false;
        }

        preset = proto;
        return true;
    }


    private static ContractServerData CreateContractData(StoreContractPrototype proto) =>
        new()
        {
            Id = proto.ID,
            Name = proto.Name,
            TargetItem = proto.TargetItem,
            Required = proto.Required,
            Progress = 0,
            Reward = proto.Reward,
            RewardCurrency = proto.RewardCurrency,
            RewardItem = proto.RewardItem,
            RewardItemCount = proto.RewardItemCount,
            Difficulty = proto.Difficulty,
            Description = proto.Description
        };
}
