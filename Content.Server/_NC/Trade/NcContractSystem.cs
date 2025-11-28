using Content.Server.Stack;
using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Server.Storage.Components;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Timing;

namespace Content.Server._NC.Trade;


public sealed class NcContractSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
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

        if (!contract.Completed)
            return false;

        // --- ищем тащимый закрытый ящик, как в массовой продаже ---
        EntityUid? crate = null;
        if (TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulled &&
            TryComp(pulled, out EntityStorageComponent? storage) &&
            !storage.Open)
            crate = pulled;

        // --- проверяем, что суммарно у игрока + в ящике реально есть Required ---
        var totalOwned = 0;
        totalOwned += _logic.GetOwned(user, contract.TargetItem);
        if (crate is { } crateUid)
            totalOwned += _logic.GetOwnedInRoot(crateUid, contract.TargetItem);

        if (totalOwned < contract.Required)
            return false; // кто-то успел забрать часть вещей до сдачи

        // --- снимаем сперва с игрока, потом с ящика ---
        if (!string.IsNullOrWhiteSpace(contract.TargetItem) && contract.Required > 0)
        {
            var left = contract.Required;

            var ownedUser = _logic.GetOwned(user, contract.TargetItem);
            var takeFromUser = Math.Min(left, ownedUser);

            if (takeFromUser > 0 &&
                !_logic.TryTakeProductUnits(user, contract.TargetItem, takeFromUser))
                return false;

            left -= takeFromUser;

            if (left > 0 && crate is { } crateUid2)
            {
                if (!_logic.TryTakeProductUnitsFromRoot(crateUid2, contract.TargetItem, left))
                    return false;
            }
        }

        // 2) Денежная награда
        if (contract.Reward > 0 && !string.IsNullOrWhiteSpace(contract.RewardCurrency))
            GiveReward(user, contract.RewardCurrency, contract.Reward);

        // 3) Предметная награда
        if (!string.IsNullOrWhiteSpace(contract.RewardItem) && contract.RewardItemCount > 0)
        {
            var xform = Transform(user);
            var coords = xform.Coordinates;

            for (var i = 0; i < contract.RewardItemCount; i++)
            {
                var spawned = _ents.SpawnEntity(contract.RewardItem, coords);

                if (_ents.HasComponent<HandsComponent>(user))
                    IoCManager.Resolve<SharedHandsSystem>().TryPickupAnyHand(user, spawned, false);
            }
        }

        // 4) удаляем контракт и подсовываем новый
        comp.Contracts.Remove(contractId);
        RefillContractsForStore(store, comp);

        return true;
    }

    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (string.IsNullOrWhiteSpace(comp.ContractsPreset))
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(comp.ContractsPreset, out var preset))
        {
            Logger.Warning(
                $"[NcContracts] Preset '{comp.ContractsPreset}' not found for refill on {ToPrettyString(uid)}");
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
