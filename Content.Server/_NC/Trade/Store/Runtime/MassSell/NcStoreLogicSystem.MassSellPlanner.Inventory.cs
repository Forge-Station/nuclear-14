using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private MassSellInventoryState BuildMassSellInventoryState(IReadOnlyList<EntityUid> items)
    {
        var stackTypeCapacity = Math.Min(items.Count, 64);
        var protoCapacity = Math.Min(items.Count, 128);
        var stackTypeProtoCapacity = Math.Min(items.Count, 32);

        var stackTypeCounts = new Dictionary<string, int>(stackTypeCapacity, StringComparer.Ordinal);
        var protoCounts = new Dictionary<string, int>(protoCapacity, StringComparer.Ordinal);
        var stackTypeProtoCounts = new Dictionary<string, Dictionary<string, int>>(stackTypeProtoCapacity, StringComparer.Ordinal);

        for (var i = 0; i < items.Count; i++)
        {
            var ent = items[i];
            if (!_ents.EntityExists(ent))
                continue;

            if (_ents.TryGetComponent(ent, out StackComponent? stack))
            {
                TrackMassSellStackEntity(ent, stack, stackTypeCounts, protoCounts, stackTypeProtoCounts);
                continue;
            }

            TrackMassSellPrototypeEntity(ent, protoCounts, 1);
        }

        return new(stackTypeCounts, protoCounts, stackTypeProtoCounts);
    }

    private void TrackMassSellStackEntity(
        EntityUid ent,
        StackComponent stack,
        Dictionary<string, int> stackTypeCounts,
        Dictionary<string, int> protoCounts,
        Dictionary<string, Dictionary<string, int>> stackTypeProtoCounts)
    {
        var count = Math.Max(stack.Count, 0);
        if (count <= 0)
            return;

        var stackTypeId = stack.StackTypeId;
        if (!string.IsNullOrWhiteSpace(stackTypeId))
            AddMassSellCount(stackTypeCounts, stackTypeId, count);

        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return;

        AddMassSellCount(protoCounts, proto.ID, count);

        if (string.IsNullOrWhiteSpace(stackTypeId))
            return;

        if (!stackTypeProtoCounts.TryGetValue(stackTypeId, out var perProto))
        {
            perProto = new Dictionary<string, int>(StringComparer.Ordinal);
            stackTypeProtoCounts[stackTypeId] = perProto;
        }

        AddMassSellCount(perProto, proto.ID, count);
    }

    private void TrackMassSellPrototypeEntity(
        EntityUid ent,
        Dictionary<string, int> protoCounts,
        int amount)
    {
        if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto)
            return;

        AddMassSellCount(protoCounts, proto.ID, amount);
    }

    private static void AddMassSellCount(Dictionary<string, int> counts, string key, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(key))
            return;

        if (!counts.TryAdd(key, amount))
            counts[key] += amount;
    }
}
