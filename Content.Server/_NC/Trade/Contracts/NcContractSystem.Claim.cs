using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    public bool TryClaim(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
        {
            Sawmill.Warning($"[Claim] Store {ToPrettyString(store)} has no NcStoreComponent.");
            return false;
        }

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
        {
            Sawmill.Warning($"[Claim] Store {ToPrettyString(store)} has no contract '{contractId}'.");
            return false;
        }

        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
        {
            Sawmill.Warning($"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has no valid targets.");
            return false;
        }

        var crateUid = _logic.GetPulledClosedCrate(user);

        var requiredByKey = new Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int>();
        foreach (var t in targets)
        {
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
            {
                Sawmill.Warning(
                    $"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has invalid target '{t.TargetItem}'.");
                return false;
            }

            var key = (t.TargetItem, t.MatchMode);
            if (!requiredByKey.TryAdd(key, t.Required))
                requiredByKey[key] = checked(requiredByKey[key] + t.Required);
        }

        // Build deep lists + snapshots exactly once per root.
        _logic.InvalidateInventoryCache(user);

        _logic.FillDeepItemsList(user, _scratchUserItems);
        _logic.FillInventorySnapshotFromItems(user, _scratchUserItems, _scratchUserSnap);
        var userSnap = _scratchUserSnap;

        var hasCrate = false;
        EntityUid? crateEntity = null;
        NcStoreLogicSystem.InventorySnapshot? crateSnap = null;

        if (crateUid is { } c0 && Exists(c0))
        {
            crateEntity = c0;
            _logic.InvalidateInventoryCache(c0);

            _logic.FillDeepItemsList(c0, _scratchCrateItems);
            _logic.FillInventorySnapshotFromItems(c0, _scratchCrateItems, _scratchCrateSnap);

            crateSnap = _scratchCrateSnap;
            hasCrate = true;
        }

        foreach (var kvp in requiredByKey)
        {
            var (protoId, matchMode) = kvp.Key;
            var required = kvp.Value;

            var ownedUser = _logic.GetOwnedFromSnapshot(userSnap, protoId, matchMode);
            var ownedInCrate = hasCrate ? _logic.GetOwnedFromSnapshot(crateSnap!, protoId, matchMode) : 0;

            if (ownedUser + ownedInCrate < required)
            {
                Sawmill.Info(
                    $"[Claim] Not enough items for '{contractId}': need {required}x {protoId} (mode={matchMode}), " +
                    $"have user={ownedUser}, crate={ownedInCrate} on {ToPrettyString(store)}.");
                return false;
            }
        }

        var orderedKeys = OrderClaimKeys(requiredByKey.Keys);

        var plan = new List<ClaimSlice>(requiredByKey.Count * 2);

        foreach (var key in orderedKeys)
        {
            var (protoId, matchMode) = key;
            var need = requiredByKey[key];
            if (need <= 0)
                continue;

            var reservedFromUser = ReserveFromSnapshot(
                userSnap,
                protoId,
                matchMode,
                need,
                out var userSlices,
                user);

            if (reservedFromUser > 0)
            {
                plan.AddRange(userSlices);
                need -= reservedFromUser;
            }

            if (need <= 0)
                continue;

            if (!hasCrate || crateEntity is not { } ce || !Exists(ce) || crateSnap == null)
            {
                Sawmill.Error(
                    $"[Claim] Missing {need}x {protoId} but pulled closed crate is missing/invalid. " +
                    $"Contract '{contractId}' on {ToPrettyString(store)}.");
                return false;
            }

            var reservedFromCrate = ReserveFromSnapshot(
                crateSnap,
                protoId,
                matchMode,
                need,
                out var crateSlices,
                ce);

            if (reservedFromCrate > 0)
            {
                plan.AddRange(crateSlices);
                need -= reservedFromCrate;
            }

            if (need > 0)
            {
                Sawmill.Error(
                    $"[Claim] Reserve failed for '{contractId}': still need {need}x {protoId} (mode={matchMode}). " +
                    $"Store={ToPrettyString(store)}.");
                return false;
            }
        }

        var exec = new Dictionary<(EntityUid Root, string ProtoId), int>();
        foreach (var s in plan)
        {
            var k = (s.Root, s.ProtoId);
            if (!exec.TryAdd(k, s.Amount))
                exec[k] = checked(exec[k] + s.Amount);
        }

        foreach (var ((root, protoId), amount) in exec)
        {
            if (amount <= 0)
                continue;

            List<EntityUid>? items = null;
            if (root == user)
                items = _scratchUserItems;
            else if (hasCrate && crateEntity is { } c1 && root == c1)
                items = _scratchCrateItems;

            if (items != null)
            {
                if (!_logic.TryTakeProductUnitsFromCachedItems(root, items, protoId, amount, PrototypeMatchMode.Exact))
                {
                    Sawmill.Error(
                        $"[Claim] Take failed for {amount}x {protoId} from {ToPrettyString(root)}. Aborting claim '{contractId}'.");
                    return false;
                }

                continue;
            }

            if (!_logic.TryTakeProductUnitsFromRoot(root, protoId, amount, PrototypeMatchMode.Exact))
            {
                Sawmill.Error(
                    $"[Claim] Take fallback failed for {amount}x {protoId} from {ToPrettyString(root)}. Aborting claim '{contractId}'.");
                return false;
            }
        }

        _logic.InvalidateInventoryCache(user);
        if (hasCrate && crateEntity is { } c2)
            _logic.InvalidateInventoryCache(c2);

        for (var i = 0; i < contract.Targets.Count; i++)
        {
            var t = contract.Targets[i];
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
                continue;

            t.Progress = t.Required;
            contract.Targets[i] = t;
        }

        foreach (var reward in contract.Rewards)
        {
            if (reward.Amount <= 0 || string.IsNullOrWhiteSpace(reward.Id))
                continue;

            switch (reward.Type)
            {
                case StoreRewardType.Currency:
                    _logic.GiveCurrency(user, reward.Id, reward.Amount);
                    break;
                case StoreRewardType.Item:
                    for (var i = 0; i < reward.Amount; i++)
                        _logic.TrySpawnProduct(reward.Id, user);
                    break;
            }
        }

        var repeatable = contract.Repeatable;

        comp.Contracts.Remove(contractId);

        if (!repeatable)
            comp.CompletedOneTimeContracts.Add(contractId);

        RefillContractsForStore(store, comp, contractId);
        return true;
    }


    private List<(string ProtoId, PrototypeMatchMode MatchMode)> OrderClaimKeys(
        Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int>.KeyCollection keys
    )
    {
        var list = new List<(string ProtoId, PrototypeMatchMode MatchMode)>(keys.Count);
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
        NcStoreLogicSystem.InventorySnapshot snap,
        string targetProtoId,
        PrototypeMatchMode matchMode,
        int need,
        out List<ClaimSlice> slices,
        EntityUid? rootOverride = null
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

            slices.Add(new(rootOverride ?? EntityUid.Invalid, targetProtoId, take));
            return take;
        }

        if (matchMode == PrototypeMatchMode.Exact)
        {
            snap.ProtoCounts.TryGetValue(targetProtoId, out var haveExact);
            if (haveExact <= 0)
                return 0;

            var take = Math.Min(haveExact, need);
            ApplyReservationExact(snap, targetProtoId, take);

            slices.Add(new(rootOverride ?? EntityUid.Invalid, targetProtoId, take));
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

            slices.Add(new(rootOverride ?? EntityUid.Invalid, exactProto, take));
            takenTotal += take;
        }

        return takenTotal;
    }

    private void ApplyReservationExact(NcStoreLogicSystem.InventorySnapshot snap, string exactProtoId, int take)
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
