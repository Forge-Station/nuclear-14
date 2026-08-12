using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem
{
    private bool TryValidateRewardPoolPrototype(
        string ownerId,
        string poolId,
        NcSupplyRewardPoolPrototype pool,
        HashSet<string>? visited = null
    )
    {
        visited ??= new(StringComparer.Ordinal);
        if (!visited.Add(poolId))
        {
            Sawmill.Warning(
                $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' creates a nested pool cycle.");
            return false;
        }

        if (pool.Entries.Count == 0)
        {
            Sawmill.Warning($"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' has no entries.");
            visited.Remove(poolId);
            return false;
        }

        var valid = true;
        var hasAtLeastOneValidEntry = false;

        for (var i = 0; i < pool.Entries.Count; i++)
            if (TryValidateRewardPoolEntry(ownerId, poolId, i, pool.Entries[i], visited))
                hasAtLeastOneValidEntry = true;
            else
                valid = false;

        visited.Remove(poolId);

        if (hasAtLeastOneValidEntry)
            return valid;

        Sawmill.Warning($"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' has no valid entries.");
        return false;
    }

    private bool TryValidateRewardPoolEntry(
        string ownerId,
        string poolId,
        int index,
        NcSupplyRewardPoolEntry entry,
        HashSet<string> visited
    )
    {
        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has non-positive weight={entry.Weight}.");
            return false;
        }

        if (entry.MaxRepeats < 0)
        {
            Sawmill.Warning(
                $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has negative max={entry.MaxRepeats}.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min >= 0, max > 0, min <= max.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (!RequireOnlyPoolRewardTarget(
                    ownerId,
                    poolId,
                    index,
                    "prototype",
                    entry.Prototype,
                    entry.Currency,
                    entry.Pool))
                    return false;

                if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                    return true;

                Sawmill.Warning(
                    $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} references missing entity prototype " +
                    $"'{entry.Prototype}'.");
                return false;

            case StoreRewardType.Currency:
                if (!RequireOnlyPoolRewardTarget(
                    ownerId,
                    poolId,
                    index,
                    "currency",
                    entry.Currency,
                    entry.Prototype,
                    entry.Pool))
                    return false;

                if (_prototypes.HasIndex<StackPrototype>(entry.Currency))
                    return true;

                Sawmill.Warning(
                    $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} references missing stack currency " +
                    $"'{entry.Currency}'.");
                return false;

            case StoreRewardType.Pool:
                if (!RequireOnlyPoolRewardTarget(
                    ownerId,
                    poolId,
                    index,
                    "pool",
                    entry.Pool,
                    entry.Prototype,
                    entry.Currency))
                    return false;

                if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(entry.Pool, out var nestedPool))
                {
                    Sawmill.Warning(
                        $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} " +
                        $"references missing nested Supply reward pool '{entry.Pool}'.");
                    return false;
                }

                return TryValidateRewardPoolPrototype(ownerId, entry.Pool, nestedPool, visited);

            case StoreRewardType.Unspecified:
                Sawmill.Warning(
                    $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning(
                    $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool RequireOnlyPoolRewardTarget(
        string ownerId,
        string poolId,
        int index,
        string expectedField,
        string expectedValue,
        string otherA,
        string otherB
    )
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
        {
            Sawmill.Warning(
                $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} requires field '{expectedField}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(otherA) && string.IsNullOrWhiteSpace(otherB))
            return true;

        Sawmill.Warning(
            $"[Contracts] Supply reward pool '{poolId}' used by '{ownerId}' entry #{index} has extra reward target fields. " +
            "Use only prototype for Item, currency for Currency, or pool for Pool.");
        return false;
    }

    private static bool IsRewardCountRange(IntRange range) => range.Min >= 0 && range.Max > 0 && range.Min <= range.Max;

    private static bool IsCountConfigured(IntRange range) => range.Min > 0 || range.Max > 0;
}
