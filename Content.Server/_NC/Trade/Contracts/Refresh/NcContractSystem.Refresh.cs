using System.Runtime.InteropServices;
using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public void RefillContractsForStore(EntityUid uid, NcStoreComponent comp, string? ignoredContractId = null)
    {
        if (!TryResolveContractPreset(uid, comp, out var preset))
            return;

        var presets = new List<StoreContractsPresetPrototype> { preset };
        var limits = MergeDifficultyLimits(presets);
        if (limits.Count == 0)
            return;

        var currentCounts = CountCurrentContracts(comp);
        var poolByDifficulty = BuildCandidatePool(presets, comp, ignoredContractId);

        foreach (var (difficulty, limit) in limits)
            ProcessDifficulty(uid, comp, difficulty, limit, currentCounts, poolByDifficulty);
    }

    private bool TryResolveContractPreset(EntityUid uid, NcStoreComponent comp, out StoreContractsPresetPrototype preset)
    {
        preset = default!;

        if (!_prototypes.TryIndex<NcStoreProfilePrototype>(comp.Profile, out var profile))
        {
            Sawmill.Warning($"[Contracts] Store profile '{comp.Profile}' not found for {ToPrettyString(uid)}.");
            return false;
        }

        if (profile.Contracts == null)
            return false;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(profile.Contracts.Value, out var resolvedPreset) ||
            resolvedPreset == null)
        {
            Sawmill.Warning(
                $"[Contracts] Contract profile '{profile.Contracts.Value}' not found for store profile '{profile.ID}'.");
            return false;
        }

        preset = resolvedPreset;
        return true;
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
        Dictionary<string, List<ContractPoolCandidate>> poolByDifficulty
    )
    {
        var current = currentCounts.GetValueOrDefault(difficulty, 0);
        var needed = limit - current;

        if (needed <= 0)
            return;

        if (!TryPrepareDifficultyPool(uid, difficulty, needed, poolByDifficulty, out var cd, out var fresh, out var recent))
            return;

        for (var i = 0; i < needed; i++)
        {
            if (!TryIssueDifficultyContract(uid, comp, fresh, recent, cd))
                break;
        }
    }

    private bool TryPrepareDifficultyPool(
        EntityUid uid,
        string difficulty,
        int needed,
        Dictionary<string, List<ContractPoolCandidate>> poolByDifficulty,
        out CooldownState cooldown,
        out List<ContractPoolCandidate> fresh,
        out List<ContractPoolCandidate>? recent)
    {
        cooldown = default!;
        fresh = default!;
        recent = null;

        if (!poolByDifficulty.TryGetValue(difficulty, out var pool) || pool.Count == 0)
            return false;

        var cooldownLimit = ComputeEffectiveContractCooldown(pool.Count, needed);
        cooldown = GetCooldownState(uid, difficulty);
        cooldown.Limit = cooldownLimit;
        cooldown.TrimToLimit();
        SplitDifficultyPoolByCooldown(pool, cooldown, cooldownLimit, out fresh, out recent);
        return true;
    }

    private static void SplitDifficultyPoolByCooldown(
        List<ContractPoolCandidate> pool,
        CooldownState cooldown,
        int cooldownLimit,
        out List<ContractPoolCandidate> fresh,
        out List<ContractPoolCandidate>? recent)
    {
        if (cooldownLimit <= 0)
        {
            fresh = new(pool);
            recent = null;
            return;
        }

        fresh = new(pool.Count);
        recent = new(pool.Count);

        foreach (var entry in pool)
        {
            if (cooldown.Contains(entry.Id))
                recent.Add(entry);
            else
                fresh.Add(entry);
        }
    }

    private bool TryIssueDifficultyContract(
        EntityUid store,
        NcStoreComponent comp,
        List<ContractPoolCandidate> fresh,
        List<ContractPoolCandidate>? recent,
        CooldownState cooldown)
    {
        var source = fresh.Count > 0 ? fresh : recent;
        if (source == null || source.Count == 0)
            return false;

        if (!TryPickAndRemoveWeighted(source, out var pick))
            return false;

        comp.Contracts[pick.Id] = CreateContractData(store, pick);
        cooldown.Push(pick.Id);
        return true;
    }

    private Dictionary<string, List<ContractPoolCandidate>> BuildCandidatePool(
        IReadOnlyList<StoreContractsPresetPrototype> presets,
        NcStoreComponent comp,
        string? ignoredContractId
    )
    {
        var flattened = GetOrBuildFlattenedPool(presets);
        var result = new Dictionary<string, List<ContractPoolCandidate>>(StringComparer.Ordinal);

        foreach (var candidate in flattened.Values)
        {
            if (candidate.Weight <= 0)
                continue;

            if (ignoredContractId != null && candidate.Id == ignoredContractId)
                continue;

            if (comp.Contracts.ContainsKey(candidate.Id))
                continue;

            if (!candidate.Repeatable && comp.CompletedOneTimeContracts.Contains(candidate.Id))
                continue;

            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(result, candidate.Difficulty, out var exists);
            if (!exists)
                list = new();

            list!.Add(candidate);
        }

        return result;
    }

    private Dictionary<string, ContractPoolCandidate> GetOrBuildFlattenedPool(
        IReadOnlyList<StoreContractsPresetPrototype> presets
    )
    {
        var cacheKey = BuildPresetPoolCacheKey(presets);
        if (_flattenedPoolCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var raw = CollectFlattenedPoolEntries(presets);
        var unique = MergeFlattenedPoolEntries(cacheKey, raw);
        _flattenedPoolCache[cacheKey] = unique;
        return unique;
    }

    private List<ContractPoolCandidate> CollectFlattenedPoolEntries(
        IReadOnlyList<StoreContractsPresetPrototype> presets)
    {
        var raw = new List<ContractPoolCandidate>();

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

            foreach (var packEntry in preset.PacksV2)
            {
                if (string.IsNullOrWhiteSpace(packEntry.Id) || packEntry.Weight <= 0)
                    continue;

                CollectFromV2PackRecursive(
                    packEntry.Id,
                    packEntry.Weight,
                    raw,
                    new HashSet<string>(StringComparer.Ordinal));
            }
        }

        return raw;
    }

    private Dictionary<string, ContractPoolCandidate> MergeFlattenedPoolEntries(
        string cacheKey,
        IReadOnlyList<ContractPoolCandidate> raw)
    {
        var unique = new Dictionary<string, ContractPoolCandidate>(StringComparer.Ordinal);

        foreach (var candidate in raw)
            AddFlattenedPoolEntry(unique, cacheKey, candidate);

        return unique;
    }

    private void AddFlattenedPoolEntry(
        Dictionary<string, ContractPoolCandidate> unique,
        string cacheKey,
        ContractPoolCandidate candidate)
    {
        if (candidate.Weight <= 0 || string.IsNullOrWhiteSpace(candidate.Id))
            return;

        if (!unique.TryGetValue(candidate.Id, out var existing))
        {
            unique[candidate.Id] = candidate;
            return;
        }

        if (existing.Kind != candidate.Kind)
        {
            Sawmill.Warning(
                $"[Contracts] Contract id collision for '{candidate.Id}' in preset set '{cacheKey}'. " +
                $"Existing kind={existing.Kind}, ignored kind={candidate.Kind}.");
            return;
        }

        var merged = SaturatingAdd(existing.Weight, candidate.Weight);
        if (merged == int.MaxValue && existing.Weight != int.MaxValue)
        {
            Sawmill.Warning(
                $"[Contracts] Total weight overflow for '{candidate.Id}' in preset set '{cacheKey}'. " +
                $"Clamping to {int.MaxValue}.");
        }

        existing.Weight = merged;
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
        List<ContractPoolCandidate> acc,
        HashSet<string> recursionStack
    )
    {
        if (string.IsNullOrWhiteSpace(packId) || weightMult <= 0)
            return;

        if (!TryEnterPackRecursion(packId, recursionStack))
            return;

        try
        {
            if (!TryResolveContractPack(packId, out var pack))
                return;

            CollectPackContractEntries(packId, weightMult, pack, acc);
            CollectPackIncludedEntries(packId, weightMult, pack, acc, recursionStack);
        }
        finally
        {
            recursionStack.Remove(packId);
        }
    }

    private void CollectFromV2PackRecursive(
        string packId,
        int weightMult,
        List<ContractPoolCandidate> acc,
        HashSet<string> recursionStack
    )
    {
        if (string.IsNullOrWhiteSpace(packId) || weightMult <= 0)
            return;

        if (!TryEnterPackRecursion(packId, recursionStack))
            return;

        try
        {
            if (!TryResolveContractPackV2(packId, out var pack))
                return;

            ValidateContractPackV2(packId, pack);
            CollectV2SupplyEntries(packId, weightMult, pack, acc);
            CollectV2IncludedEntries(packId, weightMult, pack, acc, recursionStack);
        }
        finally
        {
            recursionStack.Remove(packId);
        }
    }

    private bool TryEnterPackRecursion(string packId, HashSet<string> recursionStack)
    {
        if (recursionStack.Add(packId))
            return true;

        Sawmill.Warning($"[Contracts] Cyclic include detected for pack '{packId}'.");
        return false;
    }

    private bool TryResolveContractPack(string packId, out StoreContractPackPrototype pack)
    {
        if (_prototypes.TryIndex<StoreContractPackPrototype>(packId, out pack!))
            return true;

        Sawmill.Warning($"[Contracts] Pack '{packId}' not found. Skipping.");
        return false;
    }

    private bool TryResolveContractPackV2(string packId, out NcContractPackV2Prototype pack)
    {
        if (_prototypes.TryIndex<NcContractPackV2Prototype>(packId, out pack!))
            return true;

        Sawmill.Warning($"[ContractsV2] Pack '{packId}' not found. Skipping.");
        return false;
    }

    private void ValidateContractPackV2(string packId, NcContractPackV2Prototype pack)
    {
        if (pack.Supply.Count == 0 && pack.Includes.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Pack '{packId}' is empty. Add at least one supply entry or include.");
        }
    }

    private void CollectPackContractEntries(
        string packId,
        int weightMult,
        StoreContractPackPrototype pack,
        List<ContractPoolCandidate> acc)
    {
        foreach (var entry in pack.Contracts)
        {
            if (entry.Weight <= 0 || !_prototypes.TryIndex<StoreContractPrototype>(entry.Id, out var proto))
                continue;

            var finalWeight = MultiplyWeightsWithClamp(
                weightMult,
                entry.Weight,
                $"pack '{packId}' contract '{entry.Id}'");

            if (finalWeight <= 0)
                continue;

            acc.Add(new ContractPoolCandidate
            {
                Kind = ContractPoolCandidateKind.Legacy,
                Id = proto.ID,
                Difficulty = proto.Difficulty,
                Repeatable = proto.Repeatable,
                Weight = finalWeight,
                Legacy = proto
            });
        }
    }

    private void CollectPackIncludedEntries(
        string packId,
        int weightMult,
        StoreContractPackPrototype pack,
        List<ContractPoolCandidate> acc,
        HashSet<string> recursionStack)
    {
        foreach (var include in pack.Includes)
        {
            if (include.Weight <= 0)
                continue;

            var nestedWeight = MultiplyWeightsWithClamp(
                weightMult,
                include.Weight,
                $"pack '{packId}' include '{include.Id}'");

            if (nestedWeight > 0)
                CollectFromPackRecursive(include.Id, nestedWeight, acc, recursionStack);
        }
    }

    private void CollectV2SupplyEntries(
        string packId,
        int weightMult,
        NcContractPackV2Prototype pack,
        List<ContractPoolCandidate> acc)
    {
        foreach (var entry in pack.Supply)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                Sawmill.Warning($"[ContractsV2] Pack '{packId}' has supply entry with empty id.");
                continue;
            }

            if (entry.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Pack '{packId}' supply '{entry.Id}' has non-positive weight={entry.Weight}.");
                continue;
            }

            if (!_prototypes.TryIndex<NcSupplyContractPrototype>(entry.Id, out var proto))
            {
                Sawmill.Warning(
                    $"[ContractsV2] Pack '{packId}' references missing supply contract '{entry.Id}'.");
                continue;
            }

            if (!TryValidateSupplyContractForPool(packId, proto))
                continue;

            var finalWeight = MultiplyWeightsWithClamp(
                weightMult,
                entry.Weight,
                $"v2 pack '{packId}' supply '{entry.Id}'");

            if (finalWeight <= 0)
                continue;

            acc.Add(new ContractPoolCandidate
            {
                Kind = ContractPoolCandidateKind.SupplyV2,
                Id = proto.ID,
                Difficulty = proto.Difficulty,
                Repeatable = proto.Repeatable,
                Weight = finalWeight,
                Supply = proto
            });
        }
    }

    private void CollectV2IncludedEntries(
        string packId,
        int weightMult,
        NcContractPackV2Prototype pack,
        List<ContractPoolCandidate> acc,
        HashSet<string> recursionStack)
    {
        foreach (var include in pack.Includes)
        {
            if (string.IsNullOrWhiteSpace(include.Id))
            {
                Sawmill.Warning($"[ContractsV2] Pack '{packId}' has include entry with empty id.");
                continue;
            }

            if (include.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Pack '{packId}' include '{include.Id}' has non-positive weight={include.Weight}.");
                continue;
            }

            var nestedWeight = MultiplyWeightsWithClamp(
                weightMult,
                include.Weight,
                $"v2 pack '{packId}' include '{include.Id}'");

            if (nestedWeight > 0)
                CollectFromV2PackRecursive(include.Id, nestedWeight, acc, recursionStack);
        }
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
        List<ContractPoolCandidate> list,
        out ContractPoolCandidate picked
    )
    {
        picked = default!;

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
