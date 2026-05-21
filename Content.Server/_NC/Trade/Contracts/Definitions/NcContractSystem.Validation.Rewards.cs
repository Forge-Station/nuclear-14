using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateSupplyRewardsForPool(NcSupplyContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{proto.ID}' has no reward entries. " +
                "Use 'reward' as a list with type: Currency, Item or Pool. Contract skipped.");
            return false;
        }

        var valid = true;
        var hasAtLeastOneValidReward = false;

        for (var i = 0; i < proto.Reward.Count; i++)
            if (TryValidateSupplyRewardEntry(proto.ID, $"reward[{i}]", proto.Reward[i]))
                hasAtLeastOneValidReward = true;
            else
                valid = false;

        if (hasAtLeastOneValidReward)
            return valid;

        Sawmill.Warning(
            $"[Contracts] Supply contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }

    private bool TryValidateRetrievalRewardsForPool(NcRetrievalContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{proto.ID}' has no reward entries. " +
                "Use 'reward' as a list with type: Currency, Item or Pool. Contract skipped.");
            return false;
        }

        var valid = true;
        var hasAtLeastOneValidReward = false;

        for (var i = 0; i < proto.Reward.Count; i++)
            if (TryValidateRetrievalRewardEntry(proto.ID, $"reward[{i}]", proto.Reward[i]))
                hasAtLeastOneValidReward = true;
            else
                valid = false;

        if (hasAtLeastOneValidReward)
            return valid;

        Sawmill.Warning(
            $"[Contracts] Retrieval contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }

    private bool TryValidateSupplyRewardEntry(
        string contractId,
        string path,
        NcSupplyRewardEntry entry
    )
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] Supply contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min >= 0, max > 0, min <= max.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (!RequireOnlyRewardTarget(
                    contractId,
                    path,
                    nameof(entry.Prototype),
                    entry.Prototype,
                    entry.Currency,
                    entry.Pool))
                    return false;

                if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                    return true;

                Sawmill.Warning(
                    $"[Contracts] Supply contract '{contractId}' {path} references missing entity prototype " +
                    $"'{entry.Prototype}'.");
                return false;

            case StoreRewardType.Currency:
                if (!RequireOnlyRewardTarget(
                    contractId,
                    path,
                    nameof(entry.Currency),
                    entry.Currency,
                    entry.Prototype,
                    entry.Pool))
                    return false;

                if (_prototypes.HasIndex<StackPrototype>(entry.Currency))
                    return true;

                Sawmill.Warning(
                    $"[Contracts] Supply contract '{contractId}' {path} references missing stack currency " +
                    $"'{entry.Currency}'.");
                return false;

            case StoreRewardType.Pool:
                if (!RequireOnlyRewardTarget(
                    contractId,
                    path,
                    nameof(entry.Pool),
                    entry.Pool,
                    entry.Prototype,
                    entry.Currency))
                    return false;

                if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(entry.Pool, out var pool))
                {
                    Sawmill.Warning(
                        $"[Contracts] Supply contract '{contractId}' {path} references missing Supply reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                return TryValidateRewardPoolPrototype(contractId, entry.Pool, pool);

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[Contracts] Supply contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning(
                    $"[Contracts] Supply contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool TryValidateRetrievalRewardEntry(
        string contractId,
        string path,
        NcSupplyRewardEntry entry
    )
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min >= 0, max > 0, min <= max.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (!RequireOnlyRetrievalRewardTarget(
                    contractId,
                    path,
                    nameof(entry.Prototype),
                    entry.Prototype,
                    entry.Currency,
                    entry.Pool))
                    return false;

                if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                    return true;

                Sawmill.Warning(
                    $"[Contracts] Retrieval contract '{contractId}' {path} references missing entity prototype " +
                    $"'{entry.Prototype}'.");
                return false;

            case StoreRewardType.Currency:
                if (!RequireOnlyRetrievalRewardTarget(
                    contractId,
                    path,
                    nameof(entry.Currency),
                    entry.Currency,
                    entry.Prototype,
                    entry.Pool))
                    return false;

                if (_prototypes.HasIndex<StackPrototype>(entry.Currency))
                    return true;

                Sawmill.Warning(
                    $"[Contracts] Retrieval contract '{contractId}' {path} references missing stack currency " +
                    $"'{entry.Currency}'.");
                return false;

            case StoreRewardType.Pool:
                if (!RequireOnlyRetrievalRewardTarget(
                    contractId,
                    path,
                    nameof(entry.Pool),
                    entry.Pool,
                    entry.Prototype,
                    entry.Currency))
                    return false;

                if (!_prototypes.TryIndex<NcSupplyRewardPoolPrototype>(entry.Pool, out var pool))
                {
                    Sawmill.Warning(
                        $"[Contracts] Retrieval contract '{contractId}' {path} references missing Supply reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                return TryValidateRewardPoolPrototype(contractId, entry.Pool, pool);

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning(
                    $"[Contracts] Retrieval contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }

    private bool RequireOnlyRewardTarget(
        string contractId,
        string path,
        string expectedField,
        string expectedValue,
        string otherA,
        string otherB
    )
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
        {
            Sawmill.Warning($"[Contracts] Supply contract '{contractId}' {path} requires field '{expectedField}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(otherA) && string.IsNullOrWhiteSpace(otherB))
            return true;

        Sawmill.Warning(
            $"[Contracts] Supply contract '{contractId}' {path} has extra reward target fields. " +
            $"For each reward entry use only the field required by its type.");
        return false;
    }

    private bool RequireOnlyRetrievalRewardTarget(
        string contractId,
        string path,
        string expectedField,
        string expectedValue,
        string otherA,
        string otherB
    )
    {
        if (string.IsNullOrWhiteSpace(expectedValue))
        {
            Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} requires field '{expectedField}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(otherA) && string.IsNullOrWhiteSpace(otherB))
            return true;

        Sawmill.Warning(
            $"[Contracts] Retrieval contract '{contractId}' {path} has extra reward target fields. " +
            $"For each reward entry use only the field required by its type.");
        return false;
    }
}
