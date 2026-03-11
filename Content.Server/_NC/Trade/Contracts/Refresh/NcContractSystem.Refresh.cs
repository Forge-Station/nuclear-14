using System.Runtime.InteropServices;
using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    public void RefillContractsForStore(EntityUid uid, NcStoreComponent comp, string? ignoredContractId = null)
    {
        if (comp.ContractPresets.Count == 0)
            return;

        var presets = ResolveContractPresets(uid, comp.ContractPresets);
        if (presets.Count == 0)
            return;

        var limits = MergeDifficultyLimits(presets);
        if (limits.Count == 0)
            return;

        var currentCounts = CountCurrentContracts(comp);
        var poolByDifficulty = BuildCandidatePool(presets, comp, ignoredContractId);

        foreach (var (difficulty, limit) in limits)
            ProcessDifficulty(uid, comp, difficulty, limit, currentCounts, poolByDifficulty);
    }

    private List<StoreContractsPresetPrototype> ResolveContractPresets(EntityUid uid, IReadOnlyList<string> presetIds)
    {
        var presets = new List<StoreContractsPresetPrototype>(presetIds.Count);

        foreach (var presetId in presetIds)
        {
            if (string.IsNullOrWhiteSpace(presetId))
                continue;

            if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(presetId, out var preset))
            {
                Sawmill.Warning($"[Contracts] Preset '{presetId}' not found for {ToPrettyString(uid)}");
                continue;
            }

            presets.Add(preset);
        }

        return presets;
    }

    private static Dictionary<string, int> MergeDifficultyLimits(IReadOnlyList<StoreContractsPresetPrototype> presets)
    {
        var merged = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var preset in presets)
        {
            foreach (var (difficulty, limit) in preset.Limits)
            {
                if (string.IsNullOrWhiteSpace(difficulty) || limit <= 0)
                    continue;

                merged[difficulty] = SaturatingAdd(merged.GetValueOrDefault(difficulty, 0), limit);
            }
        }

        return merged;
    }

    private void ProcessDifficulty(
        EntityUid uid,
        NcStoreComponent comp,
        string difficulty,
        int limit,
        Dictionary<string, int> currentCounts,
        Dictionary<string, List<(StoreContractPrototype Proto, int Weight)>> poolByDifficulty
    )
    {
        var current = currentCounts.GetValueOrDefault(difficulty, 0);
        var needed = limit - current;

        if (needed <= 0)
            return;

        if (!poolByDifficulty.TryGetValue(difficulty, out var pool) || pool.Count == 0)
            return;

        var cooldownLimit = ComputeEffectiveContractCooldown(pool.Count, needed);

        var cd = GetCooldownState(uid, difficulty);
        cd.Limit = cooldownLimit;
        cd.TrimToLimit();

        List<(StoreContractPrototype Proto, int Weight)> fresh;
        List<(StoreContractPrototype Proto, int Weight)>? recent = null;

        if (cooldownLimit > 0)
        {
            fresh = new(pool.Count);
            recent = new(pool.Count);

            foreach (var e in pool)
                if (cd.Contains(e.Proto.ID))
                    recent.Add(e);
                else
                    fresh.Add(e);
        }
        else
            fresh = new(pool);

        for (var i = 0; i < needed; i++)
        {
            var source = fresh.Count > 0 ? fresh : recent;
            if (source == null || source.Count == 0)
                break;

            if (!TryPickAndRemoveWeighted(source, out var pick))
                break;

            comp.Contracts[pick.Proto.ID] = CreateContractData(uid, pick.Proto);
            cd.Push(pick.Proto.ID);
        }
    }

    private Dictionary<string, List<(StoreContractPrototype Proto, int Weight)>> BuildCandidatePool(
        IReadOnlyList<StoreContractsPresetPrototype> presets,
        NcStoreComponent comp,
        string? ignoredContractId
    )
    {
        var flattened = GetOrBuildFlattenedPool(presets);
        var result = new Dictionary<string, List<(StoreContractPrototype Proto, int Weight)>>(StringComparer.Ordinal);

        foreach (var entry in flattened.Values)
        {
            var proto = entry.Proto;
            var weight = entry.Weight;

            if (weight <= 0)
                continue;

            if (ignoredContractId != null && proto.ID == ignoredContractId)
                continue;

            if (comp.Contracts.ContainsKey(proto.ID))
                continue;

            if (!proto.Repeatable && comp.CompletedOneTimeContracts.Contains(proto.ID))
                continue;

            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(result, proto.Difficulty, out var exists);
            if (!exists)
                list = new();

            list!.Add((proto, weight));
        }

        return result;
    }

    private Dictionary<string, (StoreContractPrototype Proto, int Weight)> GetOrBuildFlattenedPool(
        IReadOnlyList<StoreContractsPresetPrototype> presets
    )
    {
        var cacheKey = BuildPresetPoolCacheKey(presets);
        if (_flattenedPoolCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var raw = new List<(StoreContractPrototype Proto, int Weight)>();

        foreach (var preset in presets)
        {
            foreach (var packEntry in preset.Packs)
            {
                if (string.IsNullOrWhiteSpace(packEntry.Id) || packEntry.Weight <= 0)
                    continue;

                CollectFromPackRecursive(
                    packEntry.Id,
                    packEntry.Weight,
                    raw,
                    new HashSet<string>(StringComparer.Ordinal));
            }
        }

        var unique = new Dictionary<string, (StoreContractPrototype Proto, int Weight)>(StringComparer.Ordinal);

        foreach (var (proto, weight) in raw)
        {
            if (weight <= 0)
                continue;

            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(unique, proto.ID, out var exists);
            if (!exists)
            {
                slot = (proto, weight);
                continue;
            }

            var merged = SaturatingAdd(slot.Weight, weight);
            if (merged == int.MaxValue && slot.Weight != int.MaxValue)
            {
                Sawmill.Warning(
                    $"[Contracts] Total weight overflow for '{proto.ID}' in preset set '{cacheKey}'. " +
                    $"Clamping to {int.MaxValue}.");
            }

            slot.Weight = merged;
        }

        _flattenedPoolCache[cacheKey] = unique;
        return unique;
    }

    private static string BuildPresetPoolCacheKey(IReadOnlyList<StoreContractsPresetPrototype> presets)
    {
        if (presets.Count == 0)
            return string.Empty;

        if (presets.Count == 1)
            return presets[0].ID;

        var ids = new string[presets.Count];
        for (var i = 0; i < presets.Count; i++)
            ids[i] = presets[i].ID;

        Array.Sort(ids, StringComparer.Ordinal);
        return string.Join('|', ids);
    }

    private static Dictionary<string, int> CountCurrentContracts(NcStoreComponent comp)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var c in comp.Contracts.Values)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, c.Difficulty, out _);
            count++;
        }

        return counts;
    }

    private CooldownState GetCooldownState(EntityUid store, string difficulty)
    {
        ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _contractCooldown,
            (store, difficulty),
            out var exists);
        if (!exists)
            state = new();

        return state!;
    }

    private void CollectFromPackRecursive(
        string packId,
        int weightMult,
        List<(StoreContractPrototype Proto, int FinalWeight)> acc,
        HashSet<string> recursionStack
    )
    {
        if (string.IsNullOrWhiteSpace(packId) || weightMult <= 0)
            return;

        if (!recursionStack.Add(packId))
        {
            Sawmill.Warning($"[Contracts] Cyclic include detected for pack '{packId}'.");
            return;
        }

        if (!_prototypes.TryIndex<StoreContractPackPrototype>(packId, out var pack))
        {
            Sawmill.Error($"[Contracts] Pack '{packId}' not found.");
            recursionStack.Remove(packId);
            return;
        }

        foreach (var entry in pack.Contracts)
        {
            if (entry.Weight <= 0)
                continue;

            if (!_prototypes.TryIndex<StoreContractPrototype>(entry.Id, out var proto))
                continue;

            var finalWeight = MultiplyWeightsWithClamp(
                weightMult,
                entry.Weight,
                $"pack '{packId}' contract '{entry.Id}'");

            if (finalWeight <= 0)
                continue;

            acc.Add((proto, finalWeight));
        }

        foreach (var include in pack.Includes)
        {
            if (include.Weight <= 0)
                continue;

            var nestedWeight = MultiplyWeightsWithClamp(
                weightMult,
                include.Weight,
                $"pack '{packId}' include '{include.Id}'");

            if (nestedWeight <= 0)
                continue;

            CollectFromPackRecursive(include.Id, nestedWeight, acc, recursionStack);
        }

        recursionStack.Remove(packId);
    }

    private int MultiplyWeightsWithClamp(int left, int right, string context)
    {
        if (left <= 0 || right <= 0)
            return 0;

        var scaled = (long) left * right;
        if (scaled <= 0)
            return 0;

        if (scaled <= int.MaxValue)
            return (int) scaled;

        Sawmill.Warning(
            $"[Contracts] Weight overflow in {context}: {left} * {right}. Clamping to {int.MaxValue}.");

        return int.MaxValue;
    }

    private static int SaturatingAdd(int left, int right)
    {
        if (left <= 0)
            return Math.Max(0, right);
        if (right <= 0)
            return left;

        var sum = (long) left + right;
        return sum >= int.MaxValue ? int.MaxValue : (int) sum;
    }

    private static int ComputeEffectiveContractCooldown(int poolCount, int needed)
    {
        if (poolCount <= 1 || needed <= 0)
            return 0;

        var upper = Math.Min(poolCount - 1, poolCount - needed);
        return Math.Max(0, upper);
    }

    private bool TryPickAndRemoveWeighted(
        List<(StoreContractPrototype Proto, int Weight)> list,
        out (StoreContractPrototype Proto, int Weight) picked
    )
    {
        picked = default;

        var total = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i].Weight;
            if (w <= 0)
                continue;

            total = SaturatingAdd(total, w);
        }

        if (total <= 0)
            return false;

        var roll = _random.Next(total);

        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i].Weight;
            if (w <= 0)
                continue;

            roll -= w;
            if (roll >= 0)
                continue;

            picked = list[i];

            var last = list.Count - 1;
            list[i] = list[last];
            list.RemoveAt(last);
            return true;
        }

        return false;
    }
}
