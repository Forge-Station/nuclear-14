using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryBuildClaimExecutionBatches(
        ClaimContext ctx,
        out Dictionary<(EntityUid Root, string ProtoId), int> exec,
        out ClaimAttemptResult fail
    )
    {
        exec = new Dictionary<(EntityUid Root, string ProtoId), int>();
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (ctx.RequiredByKey.Count == 0)
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.NoValidTargets, "RequiredByKey is empty.");
            return false;
        }

        var orderedKeys = OrderClaimKeys(ctx.RequiredByKey.Keys);
        var plan = new List<ClaimSlice>(ctx.RequiredByKey.Count * 2);

        foreach (var key in orderedKeys)
        {
            var (protoId, matchMode) = key;
            if (!ctx.RequiredByKey.TryGetValue(key, out var need) || need <= 0)
                continue;

            var reservedFromUser = ReserveFromSnapshot(
                ctx.UserSnap,
                protoId,
                matchMode,
                need,
                out var userSlices,
                ctx.User);

            if (reservedFromUser > 0)
            {
                plan.AddRange(userSlices);
                need -= reservedFromUser;
            }

            if (need <= 0)
                continue;

            if (ctx.Crate is not { } crate || !Exists(crate) || ctx.CrateSnap == null)
            {
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.MissingCrate,
                    $"Missing {need}x {protoId} (mode={matchMode}) and pulled closed crate is missing/invalid."
                );
                return false;
            }

            var reservedFromCrate = ReserveFromSnapshot(
                ctx.CrateSnap,
                protoId,
                matchMode,
                need,
                out var crateSlices,
                crate);

            if (reservedFromCrate > 0)
            {
                plan.AddRange(crateSlices);
                need -= reservedFromCrate;
            }

            if (need > 0)
            {
                fail = ClaimAttemptResult.Fail(
                    ClaimFailureReason.ReservationFailed,
                    $"Reserve failed: still need {need}x {protoId} (mode={matchMode})."
                );
                return false;
            }
        }

        foreach (var s in plan)
        {
            var k = (s.Root, s.ProtoId);
            if (!exec.TryAdd(k, s.Amount))
                exec[k] = checked(exec[k] + s.Amount);
        }

        if (exec.Count <= 0)
        {
            fail = ClaimAttemptResult.Fail(ClaimFailureReason.ReservationFailed, "Execution plan is empty.");
            return false;
        }

        return true;
    }

    private List<(string ProtoId, PrototypeMatchMode MatchMode)> OrderClaimKeys(
        IEnumerable<(string ProtoId, PrototypeMatchMode MatchMode)> keys
    )
    {
        var list = new List<(string ProtoId, PrototypeMatchMode MatchMode)>();
        foreach (var k in keys)
            list.Add(k);

        list.Sort((a, b) =>
        {
            if (a.MatchMode != b.MatchMode)
                return a.MatchMode == PrototypeMatchMode.Exact ? -1 : 1;

            if (a.MatchMode == PrototypeMatchMode.Descendants)
            {
                var da = GetProtoDepth(a.ProtoId);
                var db = GetProtoDepth(b.ProtoId);
                var cmp = db.CompareTo(da);
                if (cmp != 0)
                    return cmp;
            }

            return string.CompareOrdinal(a.ProtoId, b.ProtoId);
        });

        return list;
    }

    private int ReserveFromSnapshot(
        NcInventorySnapshot snap,
        string targetProtoId,
        PrototypeMatchMode matchMode,
        int need,
        out List<ClaimSlice> slices,
        EntityUid rootOverride
    )
    {
        slices = new();
        if (need <= 0)
            return 0;

        if (TryGetStackTypeId(targetProtoId, out var stackTypeId))
        {
            snap.StackTypeCounts.TryGetValue(stackTypeId, out var have);
            if (have <= 0)
                return 0;

            var take = Math.Min(have, need);
            var left = have - take;

            if (left > 0)
                snap.StackTypeCounts[stackTypeId] = left;
            else
                snap.StackTypeCounts.Remove(stackTypeId);

            slices.Add(new(rootOverride, targetProtoId, take));
            return take;
        }

        if (matchMode == PrototypeMatchMode.Exact)
        {
            snap.ProtoCounts.TryGetValue(targetProtoId, out var haveExact);
            if (haveExact <= 0)
                return 0;

            var take = Math.Min(haveExact, need);
            ApplyReservationExact(snap, targetProtoId, take);

            slices.Add(new(rootOverride, targetProtoId, take));
            return take;
        }

        var candidates = new List<(string ProtoId, int Count)>();
        foreach (var kvp in snap.ProtoCounts)
        {
            if (kvp.Value <= 0)
                continue;

            if (IsProtoOrDescendant(kvp.Key, targetProtoId))
                candidates.Add((kvp.Key, kvp.Value));
        }

        if (candidates.Count == 0)
            return 0;

        candidates.Sort((a, b) =>
        {
            var da = GetProtoDepth(a.ProtoId);
            var db = GetProtoDepth(b.ProtoId);
            var cmp = db.CompareTo(da);
            if (cmp != 0)
                return cmp;
            return string.CompareOrdinal(a.ProtoId, b.ProtoId);
        });

        var takenTotal = 0;
        for (var i = 0; i < candidates.Count && takenTotal < need; i++)
        {
            var (exactProto, have) = candidates[i];
            if (have <= 0)
                continue;

            var take = Math.Min(have, need - takenTotal);
            ApplyReservationExact(snap, exactProto, take);

            slices.Add(new(rootOverride, exactProto, take));
            takenTotal += take;
        }

        return takenTotal;
    }

    private void ApplyReservationExact(NcInventorySnapshot snap, string exactProtoId, int take)
    {
        if (take <= 0)
            return;

        if (snap.ProtoCounts.TryGetValue(exactProtoId, out var have))
        {
            var left = have - take;
            if (left > 0)
                snap.ProtoCounts[exactProtoId] = left;
            else
                snap.ProtoCounts.Remove(exactProtoId);
        }

        var ancestors = GetAncestorsInclusive(exactProtoId);
        foreach (var a in ancestors)
        {
            if (!snap.AncestorCounts.TryGetValue(a, out var cnt))
                continue;

            var left = cnt - take;
            if (left > 0)
                snap.AncestorCounts[a] = left;
            else
                snap.AncestorCounts.Remove(a);
        }
    }
}
