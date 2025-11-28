using Content.Server.Stack;
using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed class NcContractSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly IEntityManager _ents = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NcStoreComponent, MapInitEvent>(OnStoreMapInit);
    }

    // 🔹 Инициализация контрактов при спавне автомата
    private void OnStoreMapInit(EntityUid uid, NcStoreComponent comp, MapInitEvent args)
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

        GiveReward(user, contract.RewardCurrency, contract.Reward);
        comp.Contracts.Remove(contractId);

        return true;
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
