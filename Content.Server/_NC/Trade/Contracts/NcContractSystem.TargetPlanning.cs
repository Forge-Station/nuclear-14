using Content.Shared._NC.Trade;

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
}
