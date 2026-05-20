using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateRetrievalCargo(
        string contractId,
        int index,
        NcSupplyTargetEntry entry)
    {
        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
        var hasGroup = !string.IsNullOrWhiteSpace(entry.Group);
        var hasTagTarget = !string.IsNullOrWhiteSpace(entry.TagTarget);

        if (hasTagTarget)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{contractId}' cargo #{index} uses tagTarget '{entry.TagTarget}'. " +
                "Retrieval cargo must be spawnable; use prototype or group.");
            return false;
        }

        if (hasPrototype == hasGroup)
        {
            Sawmill.Warning(
                hasPrototype
                    ? $"[Contracts] Retrieval contract '{contractId}' cargo #{index} has both prototype and group. Use exactly one."
                    : $"[Contracts] Retrieval contract '{contractId}' cargo #{index} has neither prototype nor group.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' cargo #{index} does not define 'count'.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{contractId}' cargo #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{contractId}' cargo #{index} has non-positive weight={entry.Weight}. " +
                "Weight is used when targetCount is configured and must be > 0.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                return true;

            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{contractId}' cargo #{index} references missing entity prototype " +
                $"'{entry.Prototype}'.");
            return false;
        }

        if (!_prototypes.TryIndex<NcItemGroupPrototype>(entry.Group, out var group))
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{contractId}' cargo #{index} references missing ncItemGroup " +
                $"'{entry.Group}'. Retrieval cargo groups must reference ncItemGroup prototypes, not matcher prototypes.");
            return false;
        }

        if (!TryValidateItemGroup(contractId, entry.Group, group))
            return false;

        if (TryGetContractMatcherSpec(entry.Group, out _))
            return true;

        Sawmill.Warning(
            $"[Contracts] Retrieval contract '{contractId}' cargo #{index} references invalid item group '{entry.Group}'.");
        return false;
    }
}
