using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp, string? ignoredContractId = null) =>
        RefreshContractsInternal(uid, comp, ignoredContractId);

    private void RefreshContractsInternal(EntityUid uid, NcStoreComponent comp, string? ignoredContractId = null)
    {
        string? presetId = null;
        if (comp.ContractPresets.Count > 0)
            presetId = comp.ContractPresets[0];
        else if (!string.IsNullOrWhiteSpace(comp.LegacyContractsPreset))
            presetId = comp.LegacyContractsPreset;

        if (string.IsNullOrWhiteSpace(presetId))
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(presetId, out var mainPreset))
        {
            Sawmill.Warning($"[Contracts] Preset '{presetId}' not found for {ToPrettyString(uid)}");
            return;
        }

        var currentCounts = new Dictionary<string, int>();
        foreach (var c in comp.Contracts.Values)
        {
            currentCounts.TryAdd(c.Difficulty, 0);
            currentCounts[c.Difficulty]++;
        }

        var candidates = new List<(StoreContractPrototype Proto, int Weight)>();
        var visitedPacks = new HashSet<string>();

        foreach (var packEntry in mainPreset.Packs)
            CollectFromPackRecursive(packEntry.Id, packEntry.Weight, candidates, visitedPacks);

        var poolByDifficulty = new Dictionary<string, List<(StoreContractPrototype Proto, int Weight)>>();

        foreach (var (proto, weight) in candidates)
        {
            if (ignoredContractId != null && proto.ID == ignoredContractId)
                continue;

            if (!proto.Repeatable && comp.CompletedOneTimeContracts.Contains(proto.ID))
                continue;
            if (comp.Contracts.ContainsKey(proto.ID))
                continue;

            if (!poolByDifficulty.ContainsKey(proto.Difficulty))
                poolByDifficulty[proto.Difficulty] = new();

            poolByDifficulty[proto.Difficulty].Add((proto, weight));
        }

        foreach (var (difficulty, limit) in mainPreset.Limits)
        {
            var current = currentCounts.TryGetValue(difficulty, out var c) ? c : 0;
            var needed = limit - current;

            if (needed <= 0)
                continue;
            if (!poolByDifficulty.TryGetValue(difficulty, out var validPool) || validPool.Count == 0)
                continue;

            for (var i = 0; i < needed; i++)
            {
                if (validPool.Count == 0)
                    break;

                var pick = PickWeighted(_random, validPool, x => x.Weight);
                comp.Contracts[pick.Proto.ID] = CreateContractData(uid, pick.Proto);
                validPool.Remove(pick);
            }
        }
    }

    private void CollectFromPackRecursive(
        string packId,
        int currentWeightMult,
        List<(StoreContractPrototype Proto, int FinalWeight)> accumulator,
        HashSet<string> visitedPacks
    )
    {
        if (!visitedPacks.Add(packId))
            return;

        if (!_prototypes.TryIndex<StoreContractPackPrototype>(packId, out var pack))
        {
            Sawmill.Error($"[Contracts] Pack '{packId}' not found.");
            return;
        }

        foreach (var entry in pack.Contracts)
            if (_prototypes.TryIndex<StoreContractPrototype>(entry.Id, out var proto))
                accumulator.Add((proto, entry.Weight * currentWeightMult));

        foreach (var include in pack.Includes)
            CollectFromPackRecursive(include.Id, currentWeightMult * include.Weight, accumulator, visitedPacks);
    }
}
