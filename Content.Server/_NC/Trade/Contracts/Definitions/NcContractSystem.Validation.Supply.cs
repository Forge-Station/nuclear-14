using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateSupplyContractForPool(string packId, NcSupplyContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[Contracts] Pack '{packId}' contains a supply contract with an empty prototype id.");
            return false;
        }

        if (proto.Targets.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{proto.ID}' has no targets. " +
                "Use 'targets' with at least one entry. Contract skipped.");
            valid = false;
        }

        if (!TryValidateSupplyTargetCount(proto))
            valid = false;

        for (var i = 0; i < proto.Targets.Count; i++)
        {
            if (!TryValidateSupplyTarget(proto.ID, i, proto.Targets[i]))
                valid = false;
        }

        if (!TryValidateSupplyRewardsForPool(proto))
            valid = false;

        return valid;
    }

    private bool TryValidateSupplyTargetCount(NcSupplyContractPrototype proto)
    {
        if (!IsSupplyTargetCountConfigured(proto.TargetCount))
            return true;

        var range = proto.TargetCount;
        if (range.Min < 1 || range.Max < 1 || range.Min > range.Max)
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{proto.ID}' has invalid targetCount range " +
                $"{range.Min}..{range.Max}. Expected min >= 1, max >= min.");
            return false;
        }

        if (proto.Targets.Count > 0 && range.Max > proto.Targets.Count)
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{proto.ID}' has targetCount max={range.Max}, " +
                $"but only {proto.Targets.Count} targets are defined.");
            return false;
        }

        return true;
    }

    private bool TryValidateSupplyTarget(
        string contractId,
        int index,
        NcSupplyTargetEntry entry)
    {
        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
        var hasGroup = !string.IsNullOrWhiteSpace(entry.Group);
        var hasTagTarget = !string.IsNullOrWhiteSpace(entry.TagTarget);

        if (CountNonEmpty(entry.Prototype, entry.Group, entry.TagTarget) != 1)
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{contractId}' target #{index} must specify exactly one of prototype/group/tagTarget.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] Supply contract '{contractId}' target #{index} does not define 'count'.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{contractId}' target #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{contractId}' target #{index} has non-positive weight={entry.Weight}. " +
                "Weight is used when targetCount is configured and must be > 0.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                return true;

            Sawmill.Warning(
                $"[Contracts] Supply contract '{contractId}' target #{index} references missing entity prototype " +
                $"'{entry.Prototype}'.");
            return false;
        }

        if (hasTagTarget)
        {
            if (TryValidateTradeTagTarget(contractId, $"target #{index}", entry.TagTarget))
                return true;

            return false;
        }

        if (!_prototypes.TryIndex<NcItemGroupPrototype>(entry.Group, out var group))
        {
            Sawmill.Warning(
                $"[Contracts] Supply contract '{contractId}' target #{index} references missing ncItemGroup " +
                $"'{entry.Group}'. Supply group targets must reference ncItemGroup prototypes, not matcher prototypes.");
            return false;
        }

        if (!TryValidateItemGroup(contractId, entry.Group, group))
            return false;

        if (TryGetContractMatcherSpec(entry.Group, out _))
            return true;

        Sawmill.Warning(
            $"[Contracts] Supply contract '{contractId}' target #{index} references invalid item group '{entry.Group}'.");
        return false;
    }

    private bool TryValidateItemGroup(
        string ownerId,
        string groupId,
        NcItemGroupPrototype group)
    {
        var valid = true;
        var hasAnyEntry = false;

        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var prototypeId = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(prototypeId))
            {
                Sawmill.Warning(
                    $"[Contracts] Item group '{groupId}' used by '{ownerId}' has empty prototypes[{i}].");
                valid = false;
                continue;
            }

            hasAnyEntry = true;
            if (_prototypes.HasIndex<EntityPrototype>(prototypeId))
                continue;

            Sawmill.Warning(
                $"[Contracts] Item group '{groupId}' used by '{ownerId}' references missing entity prototype " +
                $"'{prototypeId}'.");
            valid = false;
        }

        if (hasAnyEntry)
            return valid;

        Sawmill.Warning(
            $"[Contracts] Item group '{groupId}' used by '{ownerId}' has no prototypes.");
        return false;
    }

    private static int CountNonEmpty(params string[] values)
    {
        var count = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                count++;
        }

        return count;
    }

    private bool TryValidateTradeTagTarget(string ownerId, string path, string tagTargetId)
    {
        if (!_prototypes.TryIndex<NcTradeTagPrototype>(tagTargetId, out var tagTarget))
        {
            Sawmill.Warning($"[Contracts] '{ownerId}' {path} references missing ncTradeTag '{tagTargetId}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(tagTarget.Tag) || !_prototypes.HasIndex<TagPrototype>(tagTarget.Tag))
        {
            Sawmill.Warning(
                $"[Contracts] '{ownerId}' {path} ncTradeTag '{tagTargetId}' references missing raw tag '{tagTarget.Tag}'.");
            return false;
        }

        return true;
    }
}
