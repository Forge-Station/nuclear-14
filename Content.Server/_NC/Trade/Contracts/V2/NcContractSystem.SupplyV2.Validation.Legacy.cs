using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static bool HasLegacySupplyRewardFields(NcSupplyRewardEntry entry, out string field)
    {
        if (IsLegacyTrapRangeConfigured(entry.LegacyAmount))
        {
            field = "amount";
            return true;
        }

        if (!float.IsNaN(entry.LegacyProbability))
        {
            field = "prob";
            return true;
        }

        if (!float.IsNaN(entry.LegacyChance))
        {
            field = "chance";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.LegacyId))
        {
            field = "id";
            return true;
        }

        if (entry.LegacyOptions is { Count: > 0 })
        {
            field = "options";
            return true;
        }

        field = string.Empty;
        return false;
    }

    private static bool HasLegacySupplyRewardPoolFields(NcSupplyRewardPoolEntry entry, out string field)
    {
        if (IsLegacyTrapRangeConfigured(entry.LegacyAmount))
        {
            field = "amount";
            return true;
        }

        if (!float.IsNaN(entry.LegacyProbability))
        {
            field = "prob";
            return true;
        }

        if (!float.IsNaN(entry.LegacyChance))
        {
            field = "chance";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.LegacyId))
        {
            field = "id";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.LegacyPool))
        {
            field = "pool";
            return true;
        }

        if (entry.LegacyOptions is { Count: > 0 })
        {
            field = "options";
            return true;
        }

        field = string.Empty;
        return false;
    }

    private static bool IsLegacyTrapRangeConfigured(IntRange range)
    {
        return range.Min != int.MinValue || range.Max != int.MinValue;
    }
}
