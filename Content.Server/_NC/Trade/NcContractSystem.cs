using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed class NcContractSystem : EntitySystem
{
    private const double Golden = 0.6180339887498948;
    private const double DefaultJitter = 0.06;
    private static readonly ISawmill Sawmill = Logger.GetSawmill("nccontracts");
    private readonly Dictionary<string, List<string>> _ancestorsCache = new();
    private readonly Dictionary<string, int> _depthCache = new();

    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<QuasiKey, double> _quasiPhase = new();
    [Dependency] private readonly IRobustRandom _random = default!;

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

    private static List<ContractTargetServerData> GetEffectiveTargets(ContractServerData contract)
    {
        if (contract.Targets.Count > 0)
            return contract.Targets;

        if (!string.IsNullOrWhiteSpace(contract.TargetItem) && contract.Required > 0)
        {
            return
            [
                new()
                {
                    TargetItem = contract.TargetItem,
                    Required = contract.Required,
                    Progress = contract.Progress,
                    MatchMode = contract.MatchMode
                }
            ];
        }

        return new();
    }

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

        _logic.InvalidateInventoryCache(user);
        var userSnap = _logic.BuildInventorySnapshot(user);

        var hasCrateSnap = false;
        NcStoreLogicSystem.InventorySnapshot? crateSnap = null;

        if (crateUid is { } c0 && Exists(c0))
        {
            _logic.InvalidateInventoryCache(c0);
            crateSnap = _logic.BuildInventorySnapshot(c0);
            hasCrateSnap = true;
        }
        else
            crateUid = null;

        foreach (var kvp in requiredByKey)
        {
            var (protoId, matchMode) = kvp.Key;
            var required = kvp.Value;

            var ownedUser = _logic.GetOwnedFromSnapshot(userSnap, protoId, matchMode);
            var ownedInCrate = hasCrateSnap ? _logic.GetOwnedFromSnapshot(crateSnap!, protoId, matchMode) : 0;

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

            if (crateUid is not { } crateEntity || !Exists(crateEntity) || !hasCrateSnap)
            {
                Sawmill.Error(
                    $"[Claim] Missing {need}x {protoId} but pulled closed crate is missing/invalid. " +
                    $"Contract '{contractId}' on {ToPrettyString(store)}.");
                return false;
            }

            var reservedFromCrate = ReserveFromSnapshot(
                crateSnap!,
                protoId,
                matchMode,
                need,
                out var crateSlices,
                crateEntity);

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

        if (!_logic.ExecuteContractBatch(exec))
        {
            Sawmill.Error(
                $"[Claim] ExecuteBatch failed for contract '{contractId}' on {ToPrettyString(store)}. " +
                $"(NOTE: partial consumption may have already happened)");
            return false;
        }

        for (var i = 0; i < contract.Targets.Count; i++)
        {
            var t = contract.Targets[i];
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
                continue;

            t.Progress = t.Required;
            contract.Targets[i] = t;
        }

        if (contract.RewardCurrencies is { Count: > 0, })
        {
            foreach (var kvp in contract.RewardCurrencies)
            {
                var currencyId = kvp.Key;
                var amount = kvp.Value;

                if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
                    continue;

                _logic.GiveCurrency(user, currencyId, amount);
            }
        }
        else if (contract.Reward > 0 && !string.IsNullOrWhiteSpace(contract.RewardCurrency))
            _logic.GiveCurrency(user, contract.RewardCurrency, contract.Reward);

        if (contract.RewardItems is { Count: > 0, })
        {
            foreach (var kvp in contract.RewardItems)
            {
                var protoId = kvp.Key;
                var count = kvp.Value;

                if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                    continue;

                for (var i = 0; i < count; i++)
                    _logic.TrySpawnProduct(protoId, user);
            }
        }
        else if (!string.IsNullOrWhiteSpace(contract.RewardItem) && contract.RewardItemCount > 0)
        {
            for (var i = 0; i < contract.RewardItemCount; i++)
                _logic.TrySpawnProduct(contract.RewardItem!, user);
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


    private static int GetRandomSteppedAmount(IRobustRandom random, int min, int max)
    {
        if (max < min)
            (min, max) = (max, min);

        min = Math.Max(min, 0);
        max = Math.Max(max, 0);

        if (max <= min)
            return min;

        static int TryPick(IRobustRandom random, int min, int max, int step)
        {
            if (step <= 0)
                return -1;

            var minStep = (min + step - 1) / step * step;
            var maxStep = max / step * step;

            if (maxStep < minStep)
                return -1;

            var steps = (maxStep - minStep) / step + 1;
            var idx = random.Next(steps);
            return minStep + idx * step;
        }

        var val = TryPick(random, min, max, 5);
        if (val >= 0)
            return val;

        val = TryPick(random, min, max, 3);
        if (val >= 0)
            return val;
        return min;
    }

    private static void ApplyBonusReward(
        StoreContractBonusReward bonus,
        Dictionary<string, int> rewardCurrencies,
        Dictionary<string, int> rewardItems,
        bool ignoreReplace = false
    )
    {
        if (!ignoreReplace && bonus.Mode == StoreContractBonusMode.Replace)
        {
            rewardCurrencies.Clear();
            rewardItems.Clear();
        }

        if (!string.IsNullOrWhiteSpace(bonus.Id))
        {
            var addCount = bonus.Count > 0 ? bonus.Count : 1;

            if (rewardItems.TryGetValue(bonus.Id, out var existing))
                rewardItems[bonus.Id] = existing + addCount;
            else
                rewardItems[bonus.Id] = addCount;
        }

        if (bonus.RewardCurrencies != null)
        {
            foreach (var kvp in bonus.RewardCurrencies)
            {
                var currencyId = kvp.Key;
                var amount = kvp.Value;

                if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
                    continue;

                if (rewardCurrencies.TryGetValue(currencyId, out var existing))
                    rewardCurrencies[currencyId] = existing + amount;
                else
                    rewardCurrencies[currencyId] = amount;
            }
        }

        if (bonus.RewardItems != null)
        {
            foreach (var kvp in bonus.RewardItems)
            {
                var protoId = kvp.Key;
                var count = kvp.Value;

                if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                    continue;

                if (rewardItems.TryGetValue(protoId, out var existing))
                    rewardItems[protoId] = existing + count;
                else
                    rewardItems[protoId] = count;
            }
        }
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

    private ContractServerData CreateContractData(EntityUid store, StoreContractPrototype proto)
    {
        var targets = new List<ContractTargetServerData>();

        var targetItem = proto.TargetItem ?? string.Empty;
        var required = RollSmooth(new(QuasiKeyKind.Req, store, proto.ID, null), proto.Required, 1);

        var matchMode = proto.MatchMode;

        if (proto.Targets is { Count: > 0, })
        {
            var targetCount = RollSmooth(new(QuasiKeyKind.Tc, store, proto.ID, null), proto.TargetCount, 1);

            if (targetCount <= 0)
                targetCount = 1;

            if (targetCount == 1)
            {
                var chosen = PickWeighted(_random, proto.Targets, t => t.Weight);
                {
                    targetItem = chosen.TargetItemId;

                    var chosenReq = RollSmooth(
                        new(QuasiKeyKind.TReq, store, proto.ID, chosen.TargetItemId),
                        chosen.Required,
                        1);


                    if (chosenReq > 0)
                        required = chosenReq;
                }
            }
            else
            {
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
                    var req = rolledReq > 0 ? rolledReq : required;

                    targets.Add(
                        new()
                        {
                            TargetItem = itemId,
                            Required = req,
                            Progress = 0,
                            MatchMode = matchMode
                        });
                }

                if (targets.Count > 0)
                {
                    targetItem = targets[0].TargetItem;

                    required = 0;
                    foreach (var t in targets)
                        required += t.Required;
                }
            }
        }

        var rewardCurrencies = new Dictionary<string, int>();
        var rewardItems = new Dictionary<string, int>();

        if (proto.Currencies is { Count: > 0, })
        {
            foreach (var c in proto.Currencies)
            {
                if (string.IsNullOrWhiteSpace(c.Id))
                    continue;

                var min = c.Amount.Min;
                var max = c.Amount.Max;

                if (max < min)
                    (min, max) = (max, min);

                if (min < 0)
                    min = 0;

                if (max < 0)
                    continue;

                var amount = GetRandomSteppedAmount(_random, min, max);
                if (amount <= 0)
                    continue;

                if (rewardCurrencies.TryGetValue(c.Id, out var existing))
                    rewardCurrencies[c.Id] = existing + amount;
                else
                    rewardCurrencies[c.Id] = amount;
            }
        }

        if (proto.FixedRewardItems != null)
        {
            foreach (var kvp in proto.FixedRewardItems)
            {
                var protoId = kvp.Key;
                var count = kvp.Value;

                if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                    continue;

                if (rewardItems.TryGetValue(protoId, out var existing))
                    rewardItems[protoId] = existing + count;
                else
                    rewardItems[protoId] = count;
            }
        }

        if (!string.IsNullOrWhiteSpace(proto.RewardItem) && proto.RewardItemCount > 0)
        {
            var protoId = proto.RewardItem!;
            var count = proto.RewardItemCount;

            if (rewardItems.TryGetValue(protoId, out var existing))
                rewardItems[protoId] = existing + count;
            else
                rewardItems[protoId] = count;
        }

        var allBonusRewards = new List<StoreContractBonusReward>();

        if (proto.RewardItems is { Count: > 0, })
            allBonusRewards.AddRange(proto.RewardItems);

        if (allBonusRewards.Count > 0)
        {
            var anyBonusApplied = false;

            StoreContractBonusReward ResolveEffective(StoreContractBonusReward bonus)
            {
                if (string.IsNullOrWhiteSpace(bonus.PoolId))
                    return bonus;

                if (_prototypes.TryIndex<NcContractRewardPoolPrototype>(bonus.PoolId, out var poolProto) &&
                    poolProto.Entries is { Count: > 0, })
                {
                    var poolEntry = PickWeighted(_random, poolProto.Entries, e => e.Weight);
                    return new()
                    {
                        Id = poolEntry.Id,
                        Count = poolEntry.Count,
                        Mode = poolEntry.Mode,
                        RewardCurrencies = poolEntry.RewardCurrencies != null
                            ? new Dictionary<string, int>(poolEntry.RewardCurrencies)
                            : null,
                        RewardItems = poolEntry.RewardItems != null
                            ? new Dictionary<string, int>(poolEntry.RewardItems)
                            : null
                    };
                }

                return bonus;
            }

            foreach (var bonus in allBonusRewards)
            {
                if (!bonus.Always)
                    continue;

                var effective = ResolveEffective(bonus);
                ApplyBonusReward(effective, rewardCurrencies, rewardItems, anyBonusApplied);
                anyBonusApplied = true;
            }

            var randomRewards = new List<StoreContractBonusReward>();
            foreach (var b in allBonusRewards)
                if (!b.Always)
                    randomRewards.Add(b);

            if (randomRewards.Count > 0)
            {
                var picks = RollSmooth(new(QuasiKeyKind.Bp, store, proto.ID, null), proto.BonusPickCount, 0);

                if (picks > 0)
                {
                    for (var i = 0; i < picks; i++)
                    {
                        var bonus = PickWeighted(_random, randomRewards, b => b.Weight);

                        var effective = ResolveEffective(bonus);
                        ApplyBonusReward(effective, rewardCurrencies, rewardItems, anyBonusApplied);
                        anyBonusApplied = true;
                    }
                }
            }
        }

        string? mainCurrency = null;
        var mainCurrencyAmount = 0;

        foreach (var kvp in rewardCurrencies)
        {
            var currencyId = kvp.Key;
            var amount = kvp.Value;

            if (amount <= 0 || string.IsNullOrWhiteSpace(currencyId))
                continue;

            if (amount > mainCurrencyAmount)
            {
                mainCurrency = currencyId;
                mainCurrencyAmount = amount;
            }
        }

        string? mainItem = null;
        var mainItemCount = 0;

        foreach (var kvp in rewardItems)
        {
            var protoId = kvp.Key;
            var count = kvp.Value;

            if (count <= 0 || string.IsNullOrWhiteSpace(protoId))
                continue;

            if (count > mainItemCount)
            {
                mainItem = protoId;
                mainItemCount = count;
            }
        }

        return new()
        {
            Id = proto.ID,
            Name = proto.Name,
            TargetItem = targetItem,
            Required = required,
            Progress = 0,
            MatchMode = matchMode,
            Reward = mainCurrencyAmount,
            RewardCurrency = mainCurrency ?? string.Empty,
            RewardItem = mainItem,
            RewardItemCount = mainItemCount,
            Difficulty = proto.Difficulty,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            RewardCurrencies = rewardCurrencies,
            RewardItems = rewardItems,
            Targets = targets
        };
    }


    private static T PickWeighted<T>(
        IRobustRandom random,
        IReadOnlyList<T> list,
        Func<T, int> weightSelector
    )
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

    private enum QuasiKeyKind : byte
    {
        Req,
        Tc,
        TReq,
        Bp
    }

    private readonly record struct QuasiKey(
        QuasiKeyKind Kind,
        EntityUid Store,
        string ProtoId,
        string? Extra
    );
}
