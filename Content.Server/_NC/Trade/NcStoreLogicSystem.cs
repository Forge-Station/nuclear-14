using System.Linq;
using Content.Shared._NC.Trade;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed class NcStoreLogicSystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-logic");

    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    private readonly Dictionary<EntityUid, List<EntityUid>> _inventoryCache = new();
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;

    public void InvalidateInventoryCache(EntityUid root) => _inventoryCache.Remove(root);

    public void InvalidateAllInventoryCache() => _inventoryCache.Clear();

    public int GetBalance(EntityUid user, string stackType)
    {
        var total = 0;
        foreach (var entity in EnumerateDeepItemsUnique(user))
            if (_ents.TryGetComponent(entity, out StackComponent? stack) &&
                stack.StackTypeId == stackType)
                total += stack.Count;

        return total;
    }

    private bool TryPickCurrencyForBuy(
        NcStoreComponent store,
        StoreListingPrototype listing,
        EntityUid user,
        out string currency,
        out int price
    )
    {
        currency = string.Empty;
        price = 0;

        if (listing.Cost.Count == 0)
            return false;

        var balances = new Dictionary<string, int>(store.CurrencyWhitelist.Count);
        foreach (var cur in store.CurrencyWhitelist)
            balances[cur] = GetBalance(user, cur);

        foreach (var cur in store.CurrencyWhitelist)
        {
            if (!listing.Cost.TryGetValue(cur, out var priceF))
                continue;

            var p = (int) MathF.Ceiling(priceF);
            if (p <= 0)
                continue;

            if (!balances.TryGetValue(cur, out var bal) || bal < p)
                continue;

            currency = cur;
            price = p;
            return true;
        }

        var firstCost = listing.Cost.First();
        var fallbackCur = firstCost.Key;
        var fallbackPrice = (int) MathF.Ceiling(firstCost.Value);
        if (fallbackPrice <= 0)
            return false;

        var fallbackBalance = GetBalance(user, fallbackCur);
        if (fallbackBalance < fallbackPrice)
            return false;

        currency = fallbackCur;
        price = fallbackPrice;
        return true;
    }

    private bool IsProtoOrDescendant(EntityPrototype candidate, string expectedId)
    {
        if (candidate.ID == expectedId)
            return true;

        var visited = new HashSet<string>();
        var stack = new Stack<string>();

        if (candidate.Parents != null)
        {
            foreach (var p in candidate.Parents)
                stack.Push(p);
        }

        while (stack.Count > 0)
        {
            var pid = stack.Pop();
            if (!visited.Add(pid))
                continue;

            if (pid == expectedId)
                return true;

            if (_protos.TryIndex<EntityPrototype>(pid, out var parentProto))
            {
                if (parentProto.Parents != null)
                {
                    foreach (var gp in parentProto.Parents)
                        stack.Push(gp);
                }
            }
        }

        return false;
    }


    private bool TryPickCurrencyForSell(
        NcStoreComponent store,
        StoreListingPrototype listing,
        out string currency,
        out int price
    )
    {
        currency = string.Empty;
        price = 0;

        if (listing.Cost.Count == 0)
            return false;

        foreach (var cur in store.CurrencyWhitelist)
        {
            if (!listing.Cost.TryGetValue(cur, out var priceF))
                continue;

            var p = (int) MathF.Ceiling(priceF);
            if (p <= 0)
                continue;

            currency = cur;
            price = p;
            return true;
        }

        var firstCost = listing.Cost.First();
        var fallbackCur = firstCost.Key;
        var fallbackPrice = (int) MathF.Ceiling(firstCost.Value);
        if (fallbackPrice <= 0)
            return false;

        currency = fallbackCur;
        price = fallbackPrice;
        return true;
    }

    public bool TryBuy(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        var listing = store.Listings.FirstOrDefault(x => x.Id == listingId && x.Mode == StoreMode.Buy);
        if (listing == null)
            return false;

        if (!_protos.TryIndex<EntityPrototype>(listing.ProductEntity, out _))
            return false;

        if (!TryPickCurrencyForBuy(store, listing, user, out var currency, out var unitPrice))
            return false;

        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;

        var balance = GetBalance(user, currency);
        var maxByMoney = unitPrice > 0 ? balance / unitPrice : int.MaxValue;

        var maxPossible = Math.Min(maxByRemaining, maxByMoney);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        var totalPrice = checked(unitPrice * actual);
        if (!TryTakeCurrency(user, currency, totalPrice))
            return false;

        var spawned = 0;
        for (var i = 0; i < actual; i++)
            if (TrySpawnProduct(listing.ProductEntity, user))
                spawned++;
            else
                GiveCurrency(user, currency, unitPrice);

        if (spawned <= 0)
            return false;

        if (listing.RemainingCount > 0)
            listing.RemainingCount -= spawned;

        Sawmill.Info($"TryBuy: OK {listing.ProductEntity} x{spawned} for {unitPrice} {currency} each");
        return true;
    }

    public bool TrySell(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        var listing = store.Listings.FirstOrDefault(x => x.Id == listingId && x.Mode == StoreMode.Sell);
        if (listing == null)
            return false;

        if (!TryPickCurrencyForSell(store, listing, out var currency, out var unitPrice) || unitPrice <= 0)
            return false;

        var owned = GetOwned(user, listing.ProductEntity);
        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;

        var maxPossible = Math.Min(owned, maxByRemaining);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        if (!TryTakeProductUnits(user, listing.ProductEntity, actual))
            return false;

        var total = checked(unitPrice * actual);
        GiveCurrency(user, currency, total);

        if (listing.RemainingCount > 0)
            listing.RemainingCount -= actual;

        Sawmill.Info($"TrySell: OK {listing.ProductEntity} x{actual} for {unitPrice} {currency} each");
        return true;
    }

    private int GetOwnedInternal(EntityUid root, string productProtoId)
    {
        var total = 0;

        _protos.TryIndex<EntityPrototype>(productProtoId, out var expectedProto);

        string? expectedStackType = null;
        if (expectedProto != null)
        {
            var stackName = _compFactory.GetComponentName(typeof(StackComponent));
            if (expectedProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                expectedStackType = prodStackDef.StackTypeId;
        }

        foreach (var ent in EnumerateDeepItemsUnique(root))
        {
            if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is null)
                continue;

            if (expectedStackType != null &&
                _ents.TryGetComponent(ent, out StackComponent? stack) &&
                stack.StackTypeId == expectedStackType)
            {
                total += Math.Max(stack.Count, 0);
                continue;
            }

            if (IsProtoOrDescendant(meta.EntityPrototype, productProtoId))
                total += 1;
        }

        return total;
    }

    public int GetOwned(EntityUid user, string productProtoId) => GetOwnedInternal(user, productProtoId);

    public int GetOwnedInRoot(EntityUid root, string productProtoId) => GetOwnedInternal(root, productProtoId);

    private bool TryTakeProductUnitsInternal(EntityUid root, string protoId, int amount)
    {
        if (amount <= 0)
            return true;

        InvalidateInventoryCache(root);

        string? stackType = null;
        if (_protos.TryIndex<EntityPrototype>(protoId, out var prodProto))
        {
            var stackName = _compFactory.GetComponentName(typeof(StackComponent));
            if (prodProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                stackType = prodStackDef.StackTypeId;
        }

        if (stackType == null)
        {
            var left = amount;
            foreach (var ent in EnumerateDeepItemsUnique(root))
            {
                if (left <= 0)
                    break;

                if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is null)
                    continue;

                if (!IsProtoOrDescendant(meta.EntityPrototype, protoId))
                    continue;

                if (_ents.EntityExists(ent))
                {
                    _ents.DeleteEntity(ent);
                    left -= 1;
                }
            }

            return left <= 0;
        }

        var leftStack = amount;
        foreach (var ent in EnumerateDeepItemsUnique(root))
        {
            if (leftStack <= 0)
                break;

            if (!_ents.TryGetComponent(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
                continue;

            var have = Math.Max(stack.Count, 0);
            if (have <= 0)
                continue;

            var take = Math.Min(have, leftStack);
            var newCount = have - take;
            _stacks.SetCount(ent, newCount, stack);
            if (newCount <= 0 && _ents.EntityExists(ent))
                _ents.DeleteEntity(ent);

            leftStack -= take;
        }

        return leftStack <= 0;
    }

    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount) =>
        TryTakeProductUnitsInternal(user, protoId, amount);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount) =>
        TryTakeProductUnitsInternal(root, protoId, amount);

    public bool TryExchange(
        string listingId,
        EntityUid machine,
        NcStoreComponent? store,
        EntityUid user
    )
    {
        if (store == null || store.Listings.Count == 0)
            return false;

        var listing = store.Listings.FirstOrDefault(x => x.Id == listingId && x.Mode == StoreMode.Exchange);

        if (listing == null)
            return false;

        if (string.IsNullOrEmpty(listing.ProductEntity))
            return false;

        var requiredCount = listing.RemainingCount > 0
            ? listing.RemainingCount
            : 1;

        if (requiredCount <= 0)
            return false;

        var owned = GetOwned(user, listing.ProductEntity);
        if (owned < requiredCount)
            return false;

        if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var rewardPerUnit) ||
            rewardPerUnit <= 0)
            return false;

        if (!TryTakeProductUnits(user, listing.ProductEntity, requiredCount))
            return false;

        var totalReward = checked(rewardPerUnit * requiredCount);
        GiveCurrency(user, currencyId, totalReward);

        listing.RemainingCount = 0;

        Sawmill.Info(
            $"TryExchange/Contract: {ToPrettyString(user)} выполнил контракт {listing.Id}: " +
            $"{listing.ProductEntity} x{requiredCount} -> {totalReward} {currencyId}");

        return true;
    }


    private IEnumerable<EntityUid> EnumerateDeepItemsUnique(EntityUid owner)
    {
        if (_inventoryCache.TryGetValue(owner, out var cached))
        {
            foreach (var ent in cached)
                if (_ents.EntityExists(ent))
                    yield return ent;

            yield break;
        }

        var visited = new HashSet<EntityUid>();
        var queue = new Queue<EntityUid>();
        var result = new List<EntityUid>();

        void Enqueue(EntityUid uid)
        {
            if (!visited.Add(uid))
                return;

            queue.Enqueue(uid);
            result.Add(uid);
        }


        if (_ents.TryGetComponent(owner, out InventoryComponent? inventory))
        {
            var slotEnum = new InventorySystem.InventorySlotEnumerator(inventory);
            while (slotEnum.NextItem(out var item))
                Enqueue(item);
        }

        if (_ents.TryGetComponent(owner, out ItemSlotsComponent? itemSlots))
        {
            foreach (var slot in itemSlots.Slots.Values)
                if (slot.HasItem && slot.Item.HasValue)
                    Enqueue(slot.Item.Value);
        }

        if (_ents.TryGetComponent(owner, out HandsComponent? hands))
        {
            foreach (var hand in hands.Hands.Values)
                if (hand.HeldEntity.HasValue)
                    Enqueue(hand.HeldEntity.Value);
        }

        if (_ents.TryGetComponent(owner, out ContainerManagerComponent? cmcRoot))
        {
            foreach (var container in cmcRoot.Containers.Values)
            {
                foreach (var entity in container.ContainedEntities)
                    Enqueue(entity);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (_ents.TryGetComponent(current, out ContainerManagerComponent? cmc))
            {
                foreach (var container in cmc.Containers.Values)
                {
                    foreach (var child in container.ContainedEntities)
                        Enqueue(child);
                }
            }
        }

        _inventoryCache[owner] = result;

        foreach (var ent in result)
            if (_ents.EntityExists(ent))
                yield return ent;
    }

    private bool TryTakeCurrency(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0)
            return true;

        InvalidateInventoryCache(user);

        var cands = new List<(EntityUid Ent, int Count)>();
        var total = 0;

        foreach (var ent in EnumerateDeepItemsUnique(user))
            if (_ents.TryGetComponent(ent, out StackComponent? st) &&
                st.StackTypeId == stackType)
            {
                var cnt = Math.Max(st.Count, 0);
                if (cnt <= 0)
                    continue;

                cands.Add((ent, cnt));
                total += cnt;
            }

        if (total < amount)
            return false;

        cands.Sort((a, b) => a.Count.CompareTo(b.Count));

        var left = amount;
        foreach (var (ent, have) in cands)
        {
            if (left <= 0)
                break;

            var take = Math.Min(have, left);
            if (_ents.TryGetComponent(ent, out StackComponent? st))
            {
                var newCount = st.Count - take;
                _stacks.SetCount(ent, newCount, st);
                if (newCount <= 0 && _ents.EntityExists(ent))
                    _ents.DeleteEntity(ent);
            }

            left -= take;
        }

        return left <= 0;
    }

    public void GiveCurrency(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0)
            return;

        InvalidateInventoryCache(user);

        if (!_protos.TryIndex<StackPrototype>(stackType, out var proto))
            return;

        foreach (var ent in EnumerateDeepItemsUnique(user))
        {
            if (amount <= 0)
                break;

            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                continue;

            if (proto.MaxCount is { } max)
            {
                var canAdd = Math.Max(0, max - st.Count);
                if (canAdd <= 0)
                    continue;

                var add = Math.Min(canAdd, amount);
                _stacks.SetCount(ent, st.Count + add, st);
                amount -= add;
            }
            else
            {
                _stacks.SetCount(ent, st.Count + amount, st);
                amount = 0;
                break;
            }
        }

        if (amount <= 0)
            return;

        var coords = _ents.GetComponent<TransformComponent>(user).Coordinates;

        while (amount > 0)
        {
            var add = proto.MaxCount is { } maxPerStack
                ? Math.Min(amount, Math.Max(1, maxPerStack))
                : amount;

            var spawned = _ents.SpawnEntity(proto.Spawn, coords);

            if (_ents.TryGetComponent(spawned, out StackComponent? newStack))
                _stacks.SetCount(spawned, add, newStack);

            if (_ents.HasComponent<HandsComponent>(user))
                _hands.TryPickupAnyHand(user, spawned, false);

            amount -= add;
        }
    }

    private int GetInheritanceDepth(string protoId)
    {
        if (!_protos.TryIndex<EntityPrototype>(protoId, out var proto) || proto == null)
            return 0;

        var visited = new HashSet<string>();
        var maxDepth = 0;
        var stack = new Stack<(string Id, int Depth)>();

        if (proto.Parents != null)
        {
            foreach (var pid in proto.Parents)
                if (visited.Add(pid))
                    stack.Push((pid, 1));
        }

        while (stack.Count > 0)
        {
            var (currentId, depth) = stack.Pop();
            if (depth > maxDepth)
                maxDepth = depth;

            if (_protos.TryIndex<EntityPrototype>(currentId, out var parentProto) && parentProto?.Parents != null)
            {
                foreach (var pid in parentProto.Parents)
                    if (visited.Add(pid))
                        stack.Push((pid, depth + 1));
            }
        }

        return maxDepth;
    }

    public bool TryMassSellFromContainer(
        EntityUid machine,
        NcStoreComponent store,
        EntityUid user,
        EntityUid container
    )
    {
        if (store.Listings.Count == 0)
            return false;

        InvalidateInventoryCache(container);

        var incomeByCurrency = new Dictionary<string, int>();
        var any = false;

        var sellListings = store.Listings
            .Where(l => l.Mode == StoreMode.Sell && !string.IsNullOrEmpty(l.ProductEntity))
            .OrderByDescending(l => GetInheritanceDepth(l.ProductEntity))
            .ToList();

        foreach (var listing in sellListings)
        {
            if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var unitPrice) || unitPrice <= 0)
                continue;

            var owned = GetOwnedInRoot(container, listing.ProductEntity);
            if (owned <= 0)
                continue;

            var maxByRemaining = listing.RemainingCount >= 0
                ? listing.RemainingCount
                : int.MaxValue;

            var count = Math.Min(owned, maxByRemaining);
            if (count <= 0)
                continue;

            if (!TryTakeProductUnitsFromRoot(container, listing.ProductEntity, count))
                continue;

            if (listing.RemainingCount > 0)
                listing.RemainingCount -= count;

            var total = checked(unitPrice * count);

            if (!incomeByCurrency.TryAdd(currencyId, total))
                incomeByCurrency[currencyId] += total;

            any = true;
        }

        if (!any)
            return false;

        foreach (var (currency, amount) in incomeByCurrency)
        {
            if (amount <= 0)
                continue;

            GiveCurrency(user, currency, amount);
        }

        return true;
    }


    public Dictionary<string, int> GetMassSellValue(
        NcStoreComponent store,
        EntityUid container
    )
    {
        var incomeByCurrency = new Dictionary<string, int>();

        if (store.Listings.Count == 0)
            return incomeByCurrency;

        InvalidateInventoryCache(container);


        var stackTypeCounts = new Dictionary<string, int>();
        var protoCounts = new Dictionary<string, int>();
        var protoCache = new Dictionary<string, EntityPrototype>();

        foreach (var ent in EnumerateDeepItemsUnique(container))
        {
            if (_ents.TryGetComponent(ent, out StackComponent? stack) &&
                !string.IsNullOrEmpty(stack.StackTypeId))
            {
                var cnt = Math.Max(stack.Count, 0);
                if (cnt > 0)
                {
                    if (!stackTypeCounts.TryAdd(stack.StackTypeId, cnt))
                        stackTypeCounts[stack.StackTypeId] += cnt;
                }

                continue;
            }

            if (_ents.TryGetComponent(ent, out MetaDataComponent? meta) &&
                meta.EntityPrototype is { } proto)
            {
                if (!protoCounts.TryAdd(proto.ID, 1))
                    protoCounts[proto.ID] += 1;

                protoCache[proto.ID] = proto;
            }
        }

        if (stackTypeCounts.Count == 0 && protoCounts.Count == 0)
            return incomeByCurrency;

        var protoIds = protoCounts.Count > 0
            ? protoCounts.Keys.ToArray()
            : Array.Empty<string>();


        var sellListings = store.Listings
            .Where(l =>
                l.Mode == StoreMode.Sell &&
                !string.IsNullOrEmpty(l.ProductEntity) &&
                l.RemainingCount != 0)
            .OrderByDescending(l => GetInheritanceDepth(l.ProductEntity))
            .ToArray();

        if (sellListings.Length == 0)
            return incomeByCurrency;

        var stackName = _compFactory.GetComponentName(typeof(StackComponent));


        foreach (var listing in sellListings)
        {
            if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var unitPrice) ||
                unitPrice <= 0)
                continue;

            if (!_protos.TryIndex<EntityPrototype>(listing.ProductEntity, out var prodProto))
                continue;

            var maxByRemaining = listing.RemainingCount >= 0
                ? listing.RemainingCount
                : int.MaxValue;

            if (maxByRemaining <= 0)
                continue;

            string? expectedStackType = null;

            if (prodProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                expectedStackType = prodStackDef.StackTypeId;

            if (!string.IsNullOrEmpty(expectedStackType))
            {
                if (!stackTypeCounts.TryGetValue(expectedStackType, out var available) ||
                    available <= 0)
                    continue;

                var want = maxByRemaining;
                var taken = Math.Min(available, want);
                if (taken <= 0)
                    continue;

                stackTypeCounts[expectedStackType] = available - taken;

                var total = checked(unitPrice * taken);
                if (!incomeByCurrency.TryAdd(currencyId, total))
                    incomeByCurrency[currencyId] += total;

                continue;
            }

            if (protoIds.Length == 0)
                continue;

            var wantNonStack = maxByRemaining;
            var takenNonStack = 0;

            foreach (var candId in protoIds)
            {
                if (takenNonStack >= wantNonStack)
                    break;

                if (!protoCounts.TryGetValue(candId, out var cnt) || cnt <= 0)
                    continue;

                if (!protoCache.TryGetValue(candId, out var candProto) &&
                    !_protos.TryIndex(candId, out candProto))
                    continue;

                protoCache[candId] = candProto;

                if (!IsProtoOrDescendant(candProto, listing.ProductEntity))
                    continue;

                var canTake = wantNonStack - takenNonStack;
                var take = Math.Min(cnt, canTake);
                if (take <= 0)
                    continue;

                protoCounts[candId] = cnt - take;
                takenNonStack += take;
            }

            if (takenNonStack <= 0)
                continue;

            var totalNonStack = checked(unitPrice * takenNonStack);
            if (!incomeByCurrency.TryAdd(currencyId, totalNonStack))
                incomeByCurrency[currencyId] += totalNonStack;
        }

        return incomeByCurrency;
    }


    public bool TrySpawnProduct(string protoId, EntityUid user)
    {
        try
        {
            var coords = _ents.GetComponent<TransformComponent>(user).Coordinates;
            var spawned = _ents.SpawnEntity(protoId, coords);
            if (_ents.HasComponent<HandsComponent>(user))
                _hands.TryPickupAnyHand(user, spawned, false);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error($"Spawn failed for {protoId}: {e}");
            return false;
        }
    }
}
