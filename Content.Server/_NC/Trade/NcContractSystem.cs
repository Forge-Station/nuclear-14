using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed class NcContractSystem : EntitySystem
{
    private const double Golden = 0.6180339887498948;
    private const double DefaultJitter = 0.06;
    private const int MaxRewardDepth = 6;
    private static readonly ISawmill Sawmill = Logger.GetSawmill("nccontracts");
    private readonly Dictionary<string, List<string>> _ancestorsCache = new();
    private readonly Dictionary<string, int> _depthCache = new();


    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<QuasiKey, double> _quasiPhase = new();
    [Dependency] private readonly IRobustRandom _random = default!;
    private readonly List<EntityUid> _scratchCrateItems = new();
    private readonly NcStoreLogicSystem.InventorySnapshot _scratchCrateSnap = new();
    private readonly List<EntityUid> _scratchUserItems = new();
    private readonly NcStoreLogicSystem.InventorySnapshot _scratchUserSnap = new();

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        _ancestorsCache.Clear();
        _depthCache.Clear();
        _quasiPhase.Clear();
    }

    public void InitContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (comp.Contracts.Count > 0)
            return;

        RefreshContractsInternal(uid, comp);
    }

    private static List<ContractTargetServerData> GetEffectiveTargets(ContractServerData contract) => contract.Targets;

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


    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp, string? ignoredContractId = null) =>
        RefreshContractsInternal(uid, comp, ignoredContractId);

    private void RefreshContractsInternal(EntityUid uid, NcStoreComponent comp, string? ignoredContractId = null)
    {
        string? presetId = null;
        if (comp.ContractPresets.Count > 0)
            presetId = comp.ContractPresets[0];
        else if (!string.IsNullOrWhiteSpace(comp.LegacyContractsPreset))
            presetId = comp.LegacyContractsPreset;

        if (string.IsNullOrWhiteSpace(presetId))
            return;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(presetId, out var mainPreset))
        {
            Sawmill.Warning($"[Contracts] Preset '{presetId}' not found for {ToPrettyString(uid)}");
            return;
        }

        var currentCounts = new Dictionary<string, int>();
        foreach (var c in comp.Contracts.Values)
        {
            currentCounts.TryAdd(c.Difficulty, 0);
            currentCounts[c.Difficulty]++;
        }

        var candidates = new List<(StoreContractPrototype Proto, int Weight)>();
        var visitedPacks = new HashSet<string>();

        foreach (var packEntry in mainPreset.Packs)
            CollectFromPackRecursive(packEntry.Id, packEntry.Weight, candidates, visitedPacks);

        var poolByDifficulty = new Dictionary<string, List<(StoreContractPrototype Proto, int Weight)>>();

        foreach (var (proto, weight) in candidates)
        {
            if (ignoredContractId != null && proto.ID == ignoredContractId)
                continue;

            if (!proto.Repeatable && comp.CompletedOneTimeContracts.Contains(proto.ID))
                continue;
            if (comp.Contracts.ContainsKey(proto.ID))
                continue;

            if (!poolByDifficulty.ContainsKey(proto.Difficulty))
                poolByDifficulty[proto.Difficulty] = new();

            poolByDifficulty[proto.Difficulty].Add((proto, weight));
        }

        foreach (var (difficulty, limit) in mainPreset.Limits)
        {
            var current = currentCounts.TryGetValue(difficulty, out var c) ? c : 0;
            var needed = limit - current;

            if (needed <= 0)
                continue;
            if (!poolByDifficulty.TryGetValue(difficulty, out var validPool) || validPool.Count == 0)
                continue;

            for (var i = 0; i < needed; i++)
            {
                if (validPool.Count == 0)
                    break;

                var pick = PickWeighted(_random, validPool, x => x.Weight);
                comp.Contracts[pick.Proto.ID] = CreateContractData(uid, pick.Proto);
                validPool.Remove(pick);
            }
        }
    }

    private void CollectFromPackRecursive(
        string packId,
        int currentWeightMult,
        List<(StoreContractPrototype Proto, int FinalWeight)> accumulator,
        HashSet<string> visitedPacks
    )
    {
        if (!visitedPacks.Add(packId))
            return;

        if (!_prototypes.TryIndex<StoreContractPackPrototype>(packId, out var pack))
        {
            Sawmill.Error($"[Contracts] Pack '{packId}' not found.");
            return;
        }

        foreach (var entry in pack.Contracts)
            if (_prototypes.TryIndex<StoreContractPrototype>(entry.Id, out var proto))
                accumulator.Add((proto, entry.Weight * currentWeightMult));

        foreach (var include in pack.Includes)
            CollectFromPackRecursive(include.Id, currentWeightMult * include.Weight, accumulator, visitedPacks);
    }


    private ContractServerData CreateContractData(EntityUid store, StoreContractPrototype proto)
    {
        var targets = new List<ContractTargetServerData>();

        var baseTargetItem = proto.TargetItem ?? string.Empty;
        var baseRequired = RollSmooth(new(QuasiKeyKind.Req, store, proto.ID, null), proto.Required, 1);

        if (proto.Targets is { Count: > 0, })
        {
            var targetCount = RollSmooth(new(QuasiKeyKind.Tc, store, proto.ID, null), proto.TargetCount, 1);
            if (targetCount <= 0)
                targetCount = 1;

            var pool = new List<StoreContractTargetEntry>(proto.Targets);
            var picks = Math.Min(targetCount, pool.Count);

            for (var i = 0; i < picks && pool.Count > 0; i++)
            {
                var chosen = PickWeighted(_random, pool, t => t.Weight);
                pool.Remove(chosen);

                var itemId = chosen.TargetItemId;
                var rolledReq = RollSmooth(
                    new(QuasiKeyKind.TReq, store, proto.ID, chosen.TargetItemId),
                    chosen.Required,
                    1);

                var req = rolledReq > 0 ? rolledReq : baseRequired;
                targets.Add(
                    new()
                    {
                        TargetItem = itemId,
                        Required = req,
                        Progress = 0,
                        MatchMode = proto.MatchMode
                    });
            }

            if (targets.Count == 0 && !string.IsNullOrWhiteSpace(baseTargetItem) && baseRequired > 0)
            {
                targets.Add(
                    new()
                    {
                        TargetItem = baseTargetItem,
                        Required = baseRequired,
                        Progress = 0,
                        MatchMode = proto.MatchMode
                    });
            }
        }
        else if (!string.IsNullOrWhiteSpace(baseTargetItem) && baseRequired > 0)
        {
            targets.Add(
                new()
                {
                    TargetItem = baseTargetItem,
                    Required = baseRequired,
                    Progress = 0,
                    MatchMode = proto.MatchMode
                });
        }

        var totalRequired = 0;
        foreach (var t in targets)
            totalRequired += Math.Max(0, t.Required);

        var mainTarget = targets.Count > 0 ? targets[0].TargetItem : string.Empty;

        // --- Rewards (baked) ---
        var rewards = BakeRewardsForContract(store, proto);

        return new()
        {
            Id = proto.ID,
            Name = proto.Name,
            Difficulty = proto.Difficulty,
            Description = proto.Description,
            Repeatable = proto.Repeatable,

            Targets = targets,
            TargetItem = mainTarget,
            Required = totalRequired,
            Progress = 0,

            Rewards = rewards
        };
    }


    private List<ContractRewardData> BakeRewardsForContract(EntityUid store, StoreContractPrototype proto)
    {
        if (proto.Rewards.Count == 0)
            return new();

        var baked = BakeRewardsRecursive(store, proto.ID, proto.Rewards, 0);
        return AggregateRewards(baked);
    }

    private List<ContractRewardData> BakeRewardsRecursive(
        EntityUid store,
        string contractProtoId,
        List<ContractRewardDef> blueprints,
        int depth
    )
    {
        var result = new List<ContractRewardData>();
        if (depth > MaxRewardDepth)
            return result;

        for (var i = 0; i < blueprints.Count; i++)
        {
            var bp = blueprints[i];

            if (bp.Probability < 1.0f && !_random.Prob(Math.Clamp(bp.Probability, 0f, 1f)))
                continue;
            var count = RollSmooth(
                new(QuasiKeyKind.RAmount, store, contractProtoId, $"{depth}:{i}:{bp.Type}:{bp.Id}"),
                bp.Amount,
                0);

            if (count <= 0)
                continue;

            var isPool = bp.Type == StoreRewardType.Pool || bp.Options is { Count: > 0, };

            if (isPool)
            {
                var rolled = RollPool(store, contractProtoId, bp, count, depth + 1);
                result.AddRange(rolled);
                continue;
            }

            if (string.IsNullOrWhiteSpace(bp.Id))
                continue;

            if (bp.Type != StoreRewardType.Item && bp.Type != StoreRewardType.Currency)
                continue;

            result.Add(new(bp.Type, bp.Id, count));
        }

        return result;
    }

    private List<ContractRewardData> RollPool(
        EntityUid store,
        string contractProtoId,
        ContractRewardDef poolDef,
        int rolls,
        int depth
    )
    {
        var output = new List<ContractRewardData>();
        if (depth > MaxRewardDepth)
            return output;

        List<ContractRewardDef>? options = null;
        if (poolDef.Options is { Count: > 0, })
            options = poolDef.Options;
        else if (!string.IsNullOrWhiteSpace(poolDef.Id) &&
            _prototypes.TryIndex<NcContractRewardPoolPrototype>(poolDef.Id, out var poolProto) &&
            poolProto.Entries is { Count: > 0, })
            options = poolProto.Entries;

        if (options == null || options.Count == 0)
            return output;

        var deck = new List<PoolEntry>(options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            var def = options[i];
            var key = $"{i}:{def.Type}:{def.Id}";
            deck.Add(new(def, key));
        }

        var dropCounts = new Dictionary<string, int>();

        for (var i = 0; i < rolls; i++)
        {
            if (deck.Count == 0)
                break;

            var winner = PickWeighted(_random, deck, x => x.Def.Weight);
            var key = winner.Key;

            if (!dropCounts.TryAdd(key, 1))
                dropCounts[key] = dropCounts[key] + 1;

            if (winner.Def.MaxRepeats > 0 && dropCounts[key] >= winner.Def.MaxRepeats)
                deck.Remove(winner);

            output.AddRange(BakeRewardsRecursive(store, contractProtoId, new() { winner.Def, }, depth));
        }

        return output;
    }

    private static List<ContractRewardData> AggregateRewards(List<ContractRewardData> rewards)
    {
        if (rewards.Count == 0)
            return rewards;

        var map = new Dictionary<(StoreRewardType Type, string Id), int>();

        foreach (var r in rewards)
        {
            if (r.Amount <= 0 || string.IsNullOrWhiteSpace(r.Id))
                continue;
            if (r.Type != StoreRewardType.Item && r.Type != StoreRewardType.Currency)
                continue;

            var k = (r.Type, r.Id);
            if (!map.TryAdd(k, r.Amount))
                map[k] = checked(map[k] + r.Amount);
        }

        var outList = new List<ContractRewardData>(map.Count);
        foreach (var (k, amt) in map)
        {
            if (amt <= 0)
                continue;
            outList.Add(new(k.Type, k.Id, amt));
        }

        return outList;
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

    private bool TryGetStackTypeId(string productProtoId, out string stackTypeId)
    {
        stackTypeId = string.Empty;

        if (!_prototypes.TryIndex<EntityPrototype>(productProtoId, out var expectedProto))
            return false;

        if (!expectedProto.TryGetComponent("Stack", out StackComponent? prodStackDef))
            return false;

        if (string.IsNullOrWhiteSpace(prodStackDef.StackTypeId))
            return false;

        stackTypeId = prodStackDef.StackTypeId;
        return true;
    }

    private int GetProtoDepth(string protoId)
    {
        if (_depthCache.TryGetValue(protoId, out var d))
            return d;

        if (!_prototypes.TryIndex<EntityPrototype>(protoId, out var proto))
        {
            _depthCache[protoId] = 0;
            return 0;
        }

        var best = 0;
        var parents = proto.Parents;

        if (parents is { Length: > 0, })
        {
            foreach (var p in parents)
            {
                var pd = GetProtoDepth(p) + 1;
                if (pd > best)
                    best = pd;
            }
        }

        _depthCache[protoId] = best;
        return best;
    }

    private List<string> GetAncestorsInclusive(string protoId)
    {
        if (_ancestorsCache.TryGetValue(protoId, out var list))
            return list;

        var result = new List<string> { protoId, };

        if (_prototypes.TryIndex<EntityPrototype>(protoId, out var proto))
        {
            var parents0 = proto.Parents;
            if (parents0 is { Length: > 0, })
            {
                var stack = new Stack<string>(parents0);
                var seen = new HashSet<string>();

                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!seen.Add(cur))
                        continue;

                    result.Add(cur);

                    if (_prototypes.TryIndex<EntityPrototype>(cur, out var p))
                    {
                        var parents = p.Parents;
                        if (parents is { Length: > 0, })
                        {
                            foreach (var t in parents)
                                stack.Push(t);
                        }
                    }
                }
            }
        }

        _ancestorsCache[protoId] = result;
        return result;
    }

    private bool IsProtoOrDescendant(string childProtoId, string ancestorProtoId)
    {
        if (childProtoId == ancestorProtoId)
            return true;

        if (!_prototypes.TryIndex<EntityPrototype>(childProtoId, out var child))
            return false;

        var childParents = child.Parents;
        if (childParents == null || childParents.Length == 0)
            return false;

        var stack = new Stack<string>(childParents);
        var seen = new HashSet<string>();

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur))
                continue;

            if (cur == ancestorProtoId)
                return true;

            if (_prototypes.TryIndex<EntityPrototype>(cur, out var p))
            {
                var parents = p.Parents;
                if (parents is { Length: > 0, })
                {
                    foreach (var t in parents)
                        stack.Push(t);
                }
            }
        }

        return false;
    }

    private double NextUnit() => _random.NextFloat();

    private int RollSmooth(
        QuasiKey key,
        IntRange range,
        int minClamp,
        int maxClamp = int.MaxValue,
        double jitter = DefaultJitter
    )
    {
        var min = range.Min;
        var max = range.Max;

        if (max < min)
            (min, max) = (max, min);

        min = Math.Clamp(min, minClamp, maxClamp);
        max = Math.Clamp(max, minClamp, maxClamp);

        if (max <= min)
            return min;

        if (_quasiPhase.Count > 4096)
            _quasiPhase.Clear();

        if (!_quasiPhase.TryGetValue(key, out var p))
            p = NextUnit();

        var j = (NextUnit() - 0.5) * 2.0 * jitter;
        p = p + Golden + j;
        p -= Math.Floor(p);
        _quasiPhase[key] = p;

        var buckets = max - min + 1;
        var idx = (int) Math.Floor(p * buckets);
        if (idx >= buckets)
            idx = buckets - 1;

        return min + idx;
    }

    private static T PickWeighted<T>(IRobustRandom random, IReadOnlyList<T> list, Func<T, int> weightSelector)
    {
        if (list.Count == 0)
            return default!;

        var weights = list.Count <= 128
            ? stackalloc int[list.Count]
            : new int[list.Count];

        var total = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var w = weightSelector(list[i]);
            if (w <= 0)
                w = 1;

            weights[i] = w;
            total += w;
        }

        if (total <= 0)
            return list[random.Next(list.Count)];

        var value = random.Next(total);
        var accum = 0;
        for (var i = 0; i < list.Count; i++)
        {
            accum += weights[i];
            if (value < accum)
                return list[i];
        }

        return list[^1];
    }

    private readonly record struct ClaimSlice(EntityUid Root, string ProtoId, int Amount);

    private readonly record struct PoolEntry(ContractRewardDef Def, string Key);

    private enum QuasiKeyKind : byte
    {
        Req,
        Tc,
        TReq,
        RAmount
    }

    private readonly record struct QuasiKey(QuasiKeyKind Kind, EntityUid Store, string ProtoId, string? Extra);
}
