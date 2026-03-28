using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> _claimRequiredByKeyScratch = new();
    private readonly List<(string ProtoId, PrototypeMatchMode MatchMode, int Depth)> _claimOrderedKeysScratch = new();
    private readonly Dictionary<EntityUid, int> _claimVirtualStackLeftScratch = new();

    private void BuildOrderedRequiredKeys(
        Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> requiredByKey,
        List<(string ProtoId, PrototypeMatchMode MatchMode, int Depth)> orderedKeys)
    {
        orderedKeys.Clear();

        foreach (var (key, required) in requiredByKey)
        {
            if (required <= 0)
                continue;

            orderedKeys.Add((key.ProtoId, key.MatchMode, GetProtoDepth(key.ProtoId)));
        }

        orderedKeys.Sort(static (a, b) =>
        {
            var depth = b.Depth.CompareTo(a.Depth);
            if (depth != 0)
                return depth;

            var mode = ((int) a.MatchMode).CompareTo((int) b.MatchMode);
            if (mode != 0)
                return mode;

            return string.CompareOrdinal(a.ProtoId, b.ProtoId);
        });
    }

    private void ClearClaimPlanningScratch()
    {
        _claimRequiredByKeyScratch.Clear();
        _claimOrderedKeysScratch.Clear();
        _claimVirtualStackLeftScratch.Clear();
    }

    private bool MatchesPrototypeId(string candidateId, string expectedProtoId, PrototypeMatchMode matchMode)
    {
        return matchMode == PrototypeMatchMode.Exact
            ? candidateId == expectedProtoId
            : candidateId == expectedProtoId || IsDescendantId(candidateId, expectedProtoId);
    }

    private bool CanUseContractPlanningEntity(EntityUid root, EntityUid ent, bool worldTurnInSource)
    {
        if (ent == EntityUid.Invalid || !EntityManager.EntityExists(ent))
            return false;

        return worldTurnInSource
            ? CanUseNearbyStoreTurnInEntity(ent)
            : !_logic.IsProtectedFromDirectSale(root, ent);
    }

    private int ReserveAvailableStackAmount(
        EntityUid ent,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        out bool exhausted
    )
    {
        exhausted = false;

        if (need <= 0 || !TryComp(ent, out StackComponent? stack))
            return 0;

        var available = virtualStackLeft.TryGetValue(ent, out var virtualLeft)
            ? virtualLeft
            : Math.Max(stack.Count, 0);
        if (available <= 0)
        {
            exhausted = true;
            virtualStackLeft.Remove(ent);
            return 0;
        }

        var take = Math.Min(available, need);
        if (take <= 0)
            return 0;

        var left = available - take;
        exhausted = left <= 0;

        if (left > 0)
            virtualStackLeft[ent] = left;
        else
            virtualStackLeft.Remove(ent);

        return take;
    }

    private bool TryGetPlanningEntityPrototypeId(EntityUid ent, out string prototypeId)
    {
        prototypeId = string.Empty;

        if (!TryComp(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return false;

        prototypeId = meta.EntityPrototype.ID;
        return !string.IsNullOrWhiteSpace(prototypeId);
    }
}
