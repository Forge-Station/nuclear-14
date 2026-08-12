using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem
{
    private void RefillContractsForStoreOffers(
        EntityUid store,
        NcStoreComponent comp,
        NcContractOffersPrototype offers,
        string? ignoredContractId
    )
    {
        var maxVisible = Math.Max(0, offers.MaxVisible);
        if (maxVisible <= 0 || comp.Contracts.Count >= maxVisible)
            return;

        var selectedIds = new HashSet<string>(comp.Contracts.Keys, StringComparer.Ordinal);
        if (ignoredContractId != null)
            selectedIds.Remove(ignoredContractId);

        var groups = BuildOfferGroupStates(comp, offers, ignoredContractId);
        if (groups.Count == 0)
            return;

        foreach (var group in groups)
            while (comp.Contracts.Count < maxVisible &&
                group.CurrentVisible < group.MinVisible &&
                group.CurrentVisible < group.MaxVisible)
                if (!TryIssueOfferContract(store, comp, group, selectedIds))
                    break;

        while (comp.Contracts.Count < maxVisible)
        {
            if (!TryPickOfferFillGroup(groups, selectedIds, out var group))
                break;

            if (!TryIssueOfferContract(store, comp, group, selectedIds))
                group.Candidates.Clear();
        }
    }

    private List<OfferGroupState> BuildOfferGroupStates(
        NcStoreComponent comp,
        NcContractOffersPrototype offers,
        string? ignoredContractId
    )
    {
        var states = new List<OfferGroupState>(offers.Groups.Count);

        foreach (var groupEntry in offers.Groups)
        {
            var poolId = groupEntry.Pool.Id;
            if (string.IsNullOrWhiteSpace(poolId))
                continue;

            if (!_prototypes.TryIndex<NcContractOfferPoolPrototype>(poolId, out var pool))
            {
                Sawmill.Warning($"[Contracts] contractOffers references missing offer pool '{poolId}'.");
                continue;
            }

            var group = new OfferGroupState
            {
                PoolId = pool.ID,
                PoolName = pool.Name,
                PoolOrder = pool.Order,
                PoolColor = pool.Color,
                MinVisible = Math.Max(0, groupEntry.MinVisible),
                MaxVisible = Math.Max(Math.Max(0, groupEntry.MinVisible), groupEntry.MaxVisible),
                FillWeight = Math.Max(1, groupEntry.FillWeight),
                CurrentVisible = CountCurrentOfferPoolContracts(comp, pool.ID)
            };

            BuildOfferPoolCandidates(pool, comp, ignoredContractId, group.Candidates);
            states.Add(group);
        }

        return states;
    }

    private static int CountCurrentOfferPoolContracts(NcStoreComponent comp, string poolId)
    {
        var count = 0;
        foreach (var contract in comp.Contracts.Values)
            if (string.Equals(contract.OfferPoolId, poolId, StringComparison.Ordinal))
                count++;

        return count;
    }

    private void BuildOfferPoolCandidates(
        NcContractOfferPoolPrototype pool,
        NcStoreComponent comp,
        string? ignoredContractId,
        List<ContractPoolCandidate> candidates
    )
    {
        foreach (var entry in pool.Entries)
        {
            if (entry.Weight <= 0 || string.IsNullOrWhiteSpace(entry.Id))
                continue;

            if (ignoredContractId != null && string.Equals(entry.Id, ignoredContractId, StringComparison.Ordinal))
                continue;

            if (comp.Contracts.ContainsKey(entry.Id))
                continue;

            if (!TryCreateOfferCandidate(pool, entry, out var candidate))
                continue;

            if (!candidate.Repeatable && comp.CompletedOneTimeContracts.Contains(candidate.Id))
                continue;

            candidates.Add(candidate);
        }
    }

    private bool TryCreateOfferCandidate(
        NcContractOfferPoolPrototype pool,
        NcContractOfferEntry entry,
        out ContractPoolCandidate candidate
    )
    {
        candidate = new()
        {
            Weight = entry.Weight,
            OfferPoolId = pool.ID,
            OfferPoolName = pool.Name,
            OfferPoolOrder = pool.Order,
            OfferPoolColor = pool.Color
        };

        if (!TryGetDefinitionHandler(entry.Type, out var handler))
        {
            Sawmill.Warning(
                $"[Contracts] Offer pool '{pool.ID}' contains unsupported entry type '{entry.Type}' for '{entry.Id}'.");
            return false;
        }

        return handler.TryCreateCandidate(this, pool, entry, candidate);
    }

    private bool TryPickOfferFillGroup(
        IReadOnlyList<OfferGroupState> groups,
        HashSet<string> selectedIds,
        out OfferGroupState picked
    )
    {
        picked = default!;

        var total = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.CurrentVisible >= group.MaxVisible ||
                !HasAvailableOfferCandidate(group, selectedIds))
                continue;

            total = SaturatingAdd(total, group.FillWeight);
        }

        if (total <= 0)
            return false;

        var roll = _random.Next(total);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.CurrentVisible >= group.MaxVisible ||
                !HasAvailableOfferCandidate(group, selectedIds))
                continue;

            roll -= group.FillWeight;
            if (roll >= 0)
                continue;

            picked = group;
            return true;
        }

        return false;
    }

    private static bool HasAvailableOfferCandidate(
        OfferGroupState group,
        HashSet<string> selectedIds
    )
    {
        foreach (var candidate in group.Candidates)
            if (!selectedIds.Contains(candidate.Id))
                return true;

        return false;
    }

    private bool TryIssueOfferContract(
        EntityUid store,
        NcStoreComponent comp,
        OfferGroupState group,
        HashSet<string> selectedIds
    )
    {
        RemoveUnavailableOfferCandidates(group.Candidates, selectedIds);
        if (group.Candidates.Count == 0)
            return false;

        if (!TryPickAndRemoveWeighted(group.Candidates, out var pick))
            return false;

        comp.Contracts[pick.Id] = CreateContractData(store, pick);
        group.CurrentVisible++;
        selectedIds.Add(pick.Id);

        return true;
    }

    private static void RemoveUnavailableOfferCandidates(
        List<ContractPoolCandidate> candidates,
        HashSet<string> selectedIds
    )
    {
        for (var i = candidates.Count - 1; i >= 0; i--)
            if (selectedIds.Contains(candidates[i].Id))
                candidates.RemoveAt(i);
    }

    private sealed class OfferGroupState
    {
        public readonly List<ContractPoolCandidate> Candidates = new();
        public int CurrentVisible;
        public int FillWeight;
        public int MaxVisible;
        public int MinVisible;
        public string PoolColor = string.Empty;
        public string PoolId = string.Empty;
        public string PoolName = string.Empty;
        public int PoolOrder;
    }
}
