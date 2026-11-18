using Content.Server.Stack;
using Content.Shared._Forge.ResourceExtractor;
using Content.Shared.EntityTable;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server._Forge.ResourceExtractor;

/// <summary>
/// Resolves production tables and commits complete item batches into the
/// extractor storage. No progress, fuel, UI, or motor state lives here.
/// </summary>
public sealed class ResourceExtractorOutputSystem : EntitySystem
{
    [Dependency] private readonly EntityTableSystem _entityTables = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;

    public bool ValidateProduction(
        ProtoId<ResourceExtractorProductionPrototype> productionId,
        int maxAllowedBatchSize,
        out string? error)
    {
        if (!_prototypes.TryIndex<ResourceExtractorProductionPrototype>(productionId, out var production))
        {
            error = $"production prototype {productionId} does not exist";
            return false;
        }

        if (!_prototypes.HasIndex<EntityTablePrototype>(production.OutputTable))
        {
            error = $"output table {production.OutputTable} does not exist";
            return false;
        }

        if (production.MinOutputRolls < 1 ||
            production.MaxOutputRolls < production.MinOutputRolls ||
            production.MaxOutputRolls > production.MaxOutputEntities ||
            production.MaxOutputEntities < 1 ||
            production.MaxOutputEntities > maxAllowedBatchSize)
        {
            error = $"output roll range {production.MinOutputRolls}..{production.MaxOutputRolls} " +
                    $"and max output entities {production.MaxOutputEntities} must be positive, ordered, " +
                    $"and no greater than the global batch limit {maxAllowedBatchSize}";
            return false;
        }

        error = null;
        return true;
    }

    public bool TryCreateBatch(
        EntityUid uid,
        ProtoId<ResourceExtractorProductionPrototype> productionId,
        int maxAllowedBatchSize,
        List<EntProtoId> batch,
        out string? error)
    {
        if (!ValidateProduction(productionId, maxAllowedBatchSize, out error))
            return false;

        if (!_prototypes.TryIndex(productionId, out var production) ||
            !_prototypes.TryIndex<EntityTablePrototype>(production.OutputTable, out var table))
        {
            error = $"production {productionId} or its output table does not exist";
            return false;
        }

        var rolls = _random.Next(production.MinOutputRolls, production.MaxOutputRolls + 1);
        var results = Enumerable.Range(0, rolls)
            .SelectMany(_ => _entityTables.GetSpawns(table.Table))
            .Take(production.MaxOutputEntities + 1)
            .ToList();

        if (results.Count == 0 || results.Count > production.MaxOutputEntities)
        {
            error = $"table {production.OutputTable} returned {results.Count} entries for {ToPrettyString(uid)}";
            return false;
        }

        foreach (var result in results)
        {
            if (!_prototypes.TryIndex<EntityPrototype>(result, out var prototype) ||
                !IsValidStoredOutput(prototype))
            {
                error = $"output {result} is not a storable item or a valid stack";
                return false;
            }
        }

        batch.AddRange(results);
        error = null;
        return true;
    }

    public ResourceExtractorCommitResult TryCommitBatch(
        EntityUid uid,
        IReadOnlyList<EntProtoId> batch)
    {
        if (!TryComp<StorageComponent>(uid, out var storage) ||
            !TryPlanBatch((uid, storage), batch, out var additions, out var newStacks, out var newItems))
        {
            return ResourceExtractorCommitResult.InvalidConfiguration;
        }

        var spawned = new List<EntityUid>(newStacks.Count + newItems.Count);
        foreach (var planned in newStacks)
        {
            var entity = Spawn(planned.Prototype, Transform(uid).Coordinates);
            if (!TryComp<StackComponent>(entity, out var stack))
            {
                QueueDel(entity);
                CleanupSpawned(spawned);
                return ResourceExtractorCommitResult.InvalidConfiguration;
            }

            _stacks.SetCount(entity, planned.Count, stack);
            var result = TryInsertSpawned(uid, entity, storage, spawned);
            if (result != ResourceExtractorCommitResult.Committed)
                return result;
        }

        foreach (var prototype in newItems)
        {
            var entity = Spawn(prototype, Transform(uid).Coordinates);
            var result = TryInsertSpawned(uid, entity, storage, spawned);
            if (result != ResourceExtractorCommitResult.Committed)
                return result;
        }

        // Existing stacks change only after every newly spawned entity has
        // been inserted successfully, preserving all-or-nothing batch delivery.
        foreach (var (entity, count) in additions)
        {
            if (!TryComp<StackComponent>(entity, out var stack))
            {
                CleanupSpawned(spawned);
                return ResourceExtractorCommitResult.InvalidConfiguration;
            }

            _stacks.SetCount(entity, stack.Count + count, stack);
        }

        return ResourceExtractorCommitResult.Committed;
    }

    public bool HasAnyCapacity(EntityUid uid)
    {
        if (!TryComp<StorageComponent>(uid, out var storage))
            return false;

        foreach (var entity in storage.Container.ContainedEntities)
        {
            if (TryComp<StackComponent>(entity, out var stack) && _stacks.GetAvailableSpace(stack) > 0)
                return true;
        }

        return _storage.HasSpace((uid, storage));
    }

    private ResourceExtractorCommitResult TryInsertSpawned(
        EntityUid uid,
        EntityUid entity,
        StorageComponent storage,
        List<EntityUid> spawned)
    {
        if (!_storage.CanInsert(uid, entity, out var reason, storage, ignoreStacks: true))
        {
            QueueDel(entity);
            CleanupSpawned(spawned);
            return reason == "comp-storage-insufficient-capacity"
                ? ResourceExtractorCommitResult.OutputFull
                : ResourceExtractorCommitResult.InvalidConfiguration;
        }

        if (!_storage.Insert(
                uid,
                entity,
                out _,
                storageComp: storage,
                playSound: false,
                stackAutomatically: false))
        {
            QueueDel(entity);
            CleanupSpawned(spawned);
            return ResourceExtractorCommitResult.OutputFull;
        }

        spawned.Add(entity);
        return ResourceExtractorCommitResult.Committed;
    }

    private bool TryPlanBatch(
        Entity<StorageComponent> storage,
        IReadOnlyList<EntProtoId> batch,
        out Dictionary<EntityUid, int> additions,
        out List<NewStackPlan> newStacks,
        out List<EntProtoId> newItems)
    {
        additions = new Dictionary<EntityUid, int>();
        newStacks = new List<NewStackPlan>();
        newItems = new List<EntProtoId>();

        var groups = new Dictionary<string, BatchGroup>();
        foreach (var result in batch)
        {
            if (!_prototypes.TryIndex<EntityPrototype>(result, out var prototype) ||
                !IsValidStoredOutput(prototype))
            {
                return false;
            }

            // Unit stacks can be merged into existing storage stacks. A prototype
            // that already represents a bundle (for example 10 or 500 caps) must
            // stay a separate item so its configured count is not flattened.
            if (!prototype.TryGetComponent<StackComponent>(out var stack) || stack.Count > 1)
            {
                newItems.Add(result);
                continue;
            }

            if (groups.TryGetValue(stack.StackTypeId, out var group))
                group.Count++;
            else
                groups.Add(stack.StackTypeId, new BatchGroup(result, stack.StackTypeId, 1, _stacks.GetMaxCount(stack)));
        }

        foreach (var group in groups.Values)
        {
            var remaining = group.Count;
            foreach (var entity in storage.Comp.Container.ContainedEntities)
            {
                if (!TryComp<StackComponent>(entity, out var stack) || stack.StackTypeId != group.StackType)
                    continue;

                var change = Math.Min(_stacks.GetAvailableSpace(stack), remaining);
                if (change <= 0)
                    continue;

                additions[entity] = change;
                remaining -= change;
                if (remaining == 0)
                    break;
            }

            while (remaining > 0)
            {
                var count = Math.Min(group.MaxCount, remaining);
                newStacks.Add(new NewStackPlan(group.Prototype, count));
                remaining -= count;
            }
        }

        return true;
    }

    private static bool IsValidStoredOutput(EntityPrototype prototype)
    {
        if (!prototype.TryGetComponent<ItemComponent>(out _))
            return false;

        return !prototype.TryGetComponent<StackComponent>(out var stack) ||
               (stack.Count > 0 && !string.IsNullOrEmpty(stack.StackTypeId));
    }

    private void CleanupSpawned(List<EntityUid> spawned)
    {
        foreach (var entity in spawned)
            QueueDel(entity);
    }

    private sealed class BatchGroup(
        EntProtoId prototype,
        string stackType,
        int count,
        int maxCount)
    {
        public EntProtoId Prototype { get; } = prototype;
        public string StackType { get; } = stackType;
        public int Count { get; set; } = count;
        public int MaxCount { get; } = maxCount;
    }

    private readonly record struct NewStackPlan(EntProtoId Prototype, int Count);
}

public enum ResourceExtractorCommitResult : byte
{
    Committed,
    OutputFull,
    InvalidConfiguration,
}
