using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class StoreSystemStructuredLoader
{
    private static int CountNonEmpty(params string[] values)
    {
        var count = 0;
        for (var i = 0; i < values.Length; i++)
            if (!string.IsNullOrWhiteSpace(values[i]))
                count++;

        return count;
    }

    private static string AllocateDeterministicId(string baseId, LoadContext ctx)
    {
        if (!ctx.NextSuffixByBaseId.TryGetValue(baseId, out var nextSuffix))
        {
            if (ctx.ListingIds.Add(baseId))
            {
                ctx.NextSuffixByBaseId[baseId] = 1;
                return baseId;
            }

            nextSuffix = 1;
        }

        while (true)
        {
            var candidate = $"{baseId}#{nextSuffix}";
            if (ctx.ListingIds.Add(candidate))
            {
                ctx.NextSuffixByBaseId[baseId] = nextSuffix + 1;
                return candidate;
            }

            nextSuffix++;
        }
    }
}
