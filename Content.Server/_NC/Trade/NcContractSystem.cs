using Content.Server.Stack;
using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed class NcContractSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;

    // Здесь Initialize больше не нужен, MapInit мы не подписываем.
    // Систему вызывают другие системы напрямую через методы.

    /// <summary>
    /// Инициализация контрактов для конкретного автомата.
    /// Вызывается извне (из StoreSystemStructuredLoader) на MapInit/Startup.
    /// </summary>
    public void InitContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        // Уже есть контракты — не трогаем (на будущее)
        if (comp.Contracts.Count > 0)
            return;

        if (string.IsNullOrWhiteSpace(comp.ContractsPreset))
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(comp.ContractsPreset, out var preset))
        {
            Logger.Warning($"[NcContracts] Preset '{comp.ContractsPreset}' not found for {ToPrettyString(uid)}");
            return;
        }

        comp.Contracts.Clear();

        foreach (var (key, entry) in preset.Contracts)
        {
            var id = entry.Id ?? key;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (comp.Contracts.ContainsKey(id))
                continue;

            var data = new ContractServerData
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

            comp.Contracts[id] = data;
        }
    }

    // 🔹 Забрать награду
    public bool TryClaim(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
            return false;

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
            return false;

        // Ещё раз обновили прогресс перед TryClaim снаружи (UpdateContractsProgress),
        // здесь просто проверяем.
        if (!contract.Completed)
            return false;

        // 1) Пытаемся забрать предметы, которые игрок сдаёт по контракту
        if (!string.IsNullOrWhiteSpace(contract.TargetItem) && contract.Required > 0)
        {
            // Если не удалось забрать (предметы исчезли между обновлением и сдачей) – выходим.
            if (!_logic.TryTakeProductUnits(user, contract.TargetItem, contract.Required))
                return false;
        }

        // 2) Выдаём денежную награду, если она задана
        if (contract.Reward > 0 && !string.IsNullOrWhiteSpace(contract.RewardCurrency))
            GiveReward(user, contract.RewardCurrency, contract.Reward);

        if (!string.IsNullOrWhiteSpace(contract.RewardItem) && contract.RewardItemCount > 0)
        {
            var xform = Transform(user);
            var coords = xform.Coordinates;

            for (var i = 0; i < contract.RewardItemCount; i++)
            {
                var spawned = _ents.SpawnEntity(contract.RewardItem, coords);

                // Если у игрока есть руки – пытаемся положить в руку
                if (_ents.HasComponent<HandsComponent>(user))
                    IoCManager.Resolve<SharedHandsSystem>().TryPickupAnyHand(user, spawned, false);
            }
        }

        // 3) Удаляем контракт
        comp.Contracts.Remove(contractId);

        // 4) Пытаемся выдать замену (новый контракт)
        RefillContractsForStore(store, comp);

        return true;
    }

    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (string.IsNullOrWhiteSpace(comp.ContractsPreset))
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(comp.ContractsPreset, out var preset))
        {
            Logger.Warning($"[NcContracts] Preset '{comp.ContractsPreset}' not found for refill on {ToPrettyString(uid)}");
            return;
        }

        // Пробуем добавить ОДИН новый контракт, который ещё не висит у автомата
        foreach (var (key, entry) in preset.Contracts)
        {
            var id = entry.Id ?? key;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (comp.Contracts.ContainsKey(id))
                continue; // такой уже есть

            var data = new ContractServerData
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

            comp.Contracts[id] = data;
            Logger.Info($"[NcContracts] Added new contract '{id}' to {ToPrettyString(uid)} after claim.");
            break; // добавили один – хватит
        }
    }

    // 🔹 Выдать награду стеками
    private void GiveReward(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0)
            return;

        if (!_prototypes.TryIndex<StackPrototype>(stackType, out var proto))
            return;

        foreach (var ent in EnumerateDeepItemsUnique(user))
        {
            if (amount <= 0)
                break;

            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                continue;

            if (proto.MaxCount is { } max)
            {
                var canAdd = Math.Max(0, max - st.Count);
                if (canAdd <= 0)
                    continue;

                var add = Math.Min(canAdd, amount);
                _stacks.SetCount(ent, st.Count + add, st);
                amount -= add;
            }
            else
            {
                _stacks.SetCount(ent, st.Count + amount, st);
                amount = 0;
                break;
            }
        }

        if (amount <= 0)
            return;

        // Остаток — спавним новыми стаками у ног игрока
        var xform = Transform(user);
        var coords = xform.Coordinates;

        while (amount > 0)
        {
            var add = proto.MaxCount is { } maxPerStack
                ? Math.Min(amount, Math.Max(1, maxPerStack))
                : amount;

            var spawned = _ents.SpawnEntity(proto.Spawn, coords);

            if (_ents.TryGetComponent(spawned, out StackComponent? st))
                _stacks.SetCount(spawned, add, st);

            amount -= add;
        }
    }

    // 🔹 Обход всех вложенных контейнеров
    private IEnumerable<EntityUid> EnumerateDeepItemsUnique(EntityUid owner)
    {
        var visited = new HashSet<EntityUid>();
        var queue = new Queue<EntityUid>();

        void Enqueue(EntityUid id)
        {
            if (visited.Add(id))
                queue.Enqueue(id);
        }

        Enqueue(owner);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            foreach (var mgr in _ents.GetComponents<ContainerManagerComponent>(current))
            {
                foreach (var cont in mgr.Containers.Values)
                {
                    foreach (var ent in cont.ContainedEntities)
                        Enqueue(ent);
                }
            }
        }
    }
}
