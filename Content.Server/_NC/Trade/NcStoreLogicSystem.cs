using System.Linq;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._NC.Trade;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed class NcStoreLogicSystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-logic");

    private static readonly IComparer<string> OrdinalIds = new OrdinalIdComparer();

    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    private readonly Dictionary<string, int> _inheritanceDepthCache = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _inventoryCache = new();
    private readonly Dictionary<string, string?> _productStackTypeCache = new();
    private readonly Dictionary<string, string[]> _protoAndAncestorsCache = new();

    [Dependency] private readonly IPrototypeManager _protos = default!;
    private readonly List<EntityUid> _scratchItems = new();
    private readonly Queue<EntityUid> _scratchQueue = new();
    private readonly List<EntityUid> _scratchResult = new();
    private readonly HashSet<EntityUid> _scratchVisited = new();
    [Dependency] private readonly SharedStackSystem _stacks = default!;


    public override void Initialize()
    {
        base.Initialize();

        _protos.PrototypesReloaded += OnPrototypesReloaded;
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    public override void Shutdown()
    {
        _protos.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent ev) => _inventoryCache.Remove(ev.Entity);

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        _productStackTypeCache.Clear();
        _protoAndAncestorsCache.Clear();
        _inventoryCache.Clear();
        _inheritanceDepthCache.Clear();
    }

    public void ResetFrameCache() => _inventoryCache.Clear();

    public void InvalidateInventoryCache(EntityUid root) => _inventoryCache.Remove(root);

    public EntityUid? GetPulledClosedCrate(EntityUid user) =>
        TryGetPulledClosedCrate(user, out var crate) ? crate : null;

    public bool TryGetPulledClosedCrate(EntityUid user, out EntityUid crate)
    {
        crate = default;

        if (TryComp<HandsComponent>(user, out var hands))
        {
            foreach (var hand in hands.Hands.Values)
            {
                if (hand.HeldEntity is not { } held)
                    continue;

                if (TryComp<EntityStorageComponent>(held, out var storage) && !storage.Open)
                {
                    crate = held;
                    return true;
                }
            }
        }

        if (!TryComp(user, out PullerComponent? puller) || puller.Pulling is not { } pulled)
            return false;

        if (!TryComp<EntityStorageComponent>(pulled, out var pulledStorage) || pulledStorage.Open)
            return false;

        crate = pulled;
        return true;
    }

    private PrototypeMatchMode ResolveMatchMode(string expectedProtoId, PrototypeMatchMode configured)
    {
        if (configured == PrototypeMatchMode.Descendants)
            return PrototypeMatchMode.Descendants;

        if (_protos.TryIndex<EntityPrototype>(expectedProtoId, out var expectedProto) && expectedProto.Abstract)
            return PrototypeMatchMode.Descendants;

        return PrototypeMatchMode.Exact;
    }


    public int GetBalance(EntityUid user, string stackType)
    {
        var total = 0;
        foreach (var entity in EnumerateDeepItemsUnique(user))
            if (_ents.TryGetComponent(entity, out StackComponent? stack) &&
                stack.StackTypeId == stackType)
                total += stack.Count;

        return total;
    }

    public InventorySnapshot BuildInventorySnapshot(EntityUid root)
    {
        var snap = new InventorySnapshot();
        FillInventorySnapshot(root, snap);
        return snap;
    }

    // НОВЫЙ ОПТИМИЗИРОВАННЫЙ МЕТОД
    public void FillInventorySnapshot(EntityUid root, InventorySnapshot buffer)
    {
        buffer.Clear();

        foreach (var ent in EnumerateDeepItemsUnique(root))
        {
            if (IsProtectedFromDirectSale(root, ent))
                continue;

            if (_ents.TryGetComponent(ent, out StackComponent? stack))
            {
                var cnt = Math.Max(stack.Count, 0);
                if (cnt > 0 && !string.IsNullOrWhiteSpace(stack.StackTypeId))
                {
                    buffer.StackTypeCounts.TryGetValue(stack.StackTypeId, out var prev);
                    buffer.StackTypeCounts[stack.StackTypeId] = prev + cnt;
                }

                continue;
            }

            if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is null)
                continue;

            var proto = meta.EntityPrototype;

            if (!buffer.ProtoCounts.TryAdd(proto.ID, 1))
                buffer.ProtoCounts[proto.ID] += 1;

            foreach (var id in GetProtoAndAncestors(proto))
            {
                buffer.AncestorCounts.TryGetValue(id, out var prev);
                buffer.AncestorCounts[id] = prev + 1;
            }
        }
    }


    public int GetOwnedFromSnapshot(in InventorySnapshot snapshot, string productProtoId) =>
        GetOwnedFromSnapshot(snapshot, productProtoId, PrototypeMatchMode.Exact);

    public int GetOwnedFromSnapshot(in InventorySnapshot snapshot, string productProtoId, PrototypeMatchMode matchMode)
    {
        var stackType = GetProductStackType(productProtoId);
        if (stackType != null)
            return snapshot.StackTypeCounts.TryGetValue(stackType, out var cnt) ? cnt : 0;

        var effective = ResolveMatchMode(productProtoId, matchMode);

        if (effective == PrototypeMatchMode.Descendants)
            return snapshot.AncestorCounts.TryGetValue(productProtoId, out var units) ? units : 0;

        return snapshot.ProtoCounts.TryGetValue(productProtoId, out var exact) ? exact : 0;
    }

    private string? GetProductStackType(string productProtoId)
    {
        if (_productStackTypeCache.TryGetValue(productProtoId, out var cached))
            return cached;

        string? stackType = null;

        if (_protos.TryIndex<EntityPrototype>(productProtoId, out var expectedProto))
        {
            var stackName = _compFactory.GetComponentName(typeof(StackComponent));
            if (expectedProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                stackType = prodStackDef.StackTypeId;
        }

        _productStackTypeCache[productProtoId] = stackType;
        return stackType;
    }

    private string[] GetProtoAndAncestors(EntityPrototype proto)
    {
        var id = proto.ID;
        if (_protoAndAncestorsCache.TryGetValue(id, out var cached))
            return cached;

        var visited = new HashSet<string>();
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(id);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!visited.Add(cur))
                continue;
            result.Add(cur);

            if (!_protos.TryIndex<EntityPrototype>(cur, out var curProto) || curProto.Parents == null)
                continue;

            foreach (var p in curProto.Parents)
                if (!string.IsNullOrWhiteSpace(p))
                    stack.Push(p);
        }

        var arr = result.ToArray();
        _protoAndAncestorsCache[id] = arr;
        return arr;
    }

    private bool TryPickCurrencyForBuy(
        NcStoreComponent store,
        StoreListingPrototype listing,
        in InventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    )
    {
        currency = string.Empty;
        unitPrice = 0;
        balance = 0;

        if (listing.Cost.Count == 0)
            return false;
        foreach (var cur in store.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;

            if (!listing.Cost.TryGetValue(cur, out var priceF))
                continue;

            var p = (int) MathF.Ceiling(priceF);
            if (p <= 0)
                continue;

            var bal = snapshot.StackTypeCounts.TryGetValue(cur, out var b) ? b : 0;
            if (bal < p)
                continue;

            currency = cur;
            unitPrice = p;
            balance = bal;
            return true;
        }

        var firstCost = listing.Cost.First();
        var fallbackCur = firstCost.Key;
        var fallbackPrice = (int) MathF.Ceiling(firstCost.Value);
        if (fallbackPrice <= 0)
            return false;

        var fallbackBal = snapshot.StackTypeCounts.TryGetValue(fallbackCur, out var fb) ? fb : 0;
        if (fallbackBal < fallbackPrice)
            return false;

        currency = fallbackCur;
        unitPrice = fallbackPrice;
        balance = fallbackBal;
        return true;
    }

    private bool IsProtoOrDescendant(EntityPrototype candidate, string expectedId)
    {
        if (candidate.ID == expectedId)
            return true;

        var ancestors = GetProtoAndAncestors(candidate);
        foreach (var t in ancestors)
            if (t == expectedId)
                return true;

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
            if (string.IsNullOrWhiteSpace(cur))
                continue;

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

        if (!store.ListingIndex.TryGetValue(NcStoreComponent.MakeListingKey(StoreMode.Buy, listingId), out var listing))
            return false;

        if (!_protos.TryIndex<EntityPrototype>(listing.ProductEntity, out var proto))
            return false;

        InvalidateInventoryCache(user);
        var snap = BuildInventorySnapshot(user);

        if (!TryPickCurrencyForBuy(store, listing, snap, out var currency, out var unitPrice, out var balance))
            return false;

        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;
        var maxByMoney = unitPrice > 0 ? balance / unitPrice : int.MaxValue;

        var maxPossible = Math.Min(maxByRemaining, maxByMoney);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        var totalPriceL = (long) unitPrice * actual;
        if (totalPriceL > int.MaxValue)
            return false;

        var totalPrice = (int) totalPriceL;
        if (!TryTakeCurrency(user, currency, totalPrice))
            return false;

        var spawnedTotal = 0;
        var userCoords = _ents.GetComponent<TransformComponent>(user).Coordinates;

        if (proto.TryGetComponent("Stack", out StackComponent? stackComp))
        {
            var maxPerStack = int.MaxValue;
            if (_protos.TryIndex<StackPrototype>(stackComp.StackTypeId, out var stackTypeProto))
                maxPerStack = stackTypeProto.MaxCount ?? int.MaxValue;

            if (maxPerStack <= 0)
                maxPerStack = 1;

            var remainingToSpawn = actual;

            while (remainingToSpawn > 0)
            {
                var amount = Math.Min(remainingToSpawn, maxPerStack);
                try
                {
                    var spawned = _ents.SpawnEntity(listing.ProductEntity, userCoords);
                    if (_ents.TryGetComponent(spawned, out StackComponent? spawnedStack))
                        _stacks.SetCount(spawned, amount, spawnedStack);

                    var pickedUp = false;
                    if (_ents.HasComponent<HandsComponent>(user))
                        pickedUp = _hands.TryPickupAnyHand(user, spawned, false);

                    if (!pickedUp && TryGetPulledClosedCrate(user, out var crate) && Exists(crate))
                    {
                        _entityStorage.Insert(spawned, crate);
                        InvalidateInventoryCache(crate);
                    }

                    spawnedTotal += amount;
                    remainingToSpawn -= amount;
                }
                catch (Exception e)
                {
                    Sawmill.Error($"Spawn failed during bulk buy: {e}");
                    break;
                }
            }
        }
        else
        {
            for (var i = 0; i < actual; i++)
                if (TrySpawnProduct(listing.ProductEntity, user))
                    spawnedTotal++;
                else
                    GiveCurrency(user, currency, unitPrice);
        }

        InvalidateInventoryCache(user);

        if (spawnedTotal <= 0)
            return false;

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - spawnedTotal);

        Sawmill.Info($"TryBuy: OK {listing.ProductEntity} x{spawnedTotal} for {unitPrice} {currency} each");
        return true;
    }

    public bool TrySell(string listingId, EntityUid machine, NcStoreComponent? store, EntityUid user, int count = 1)
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Sell, listingId),
            out var listing))
            return false;


        if (!TryPickCurrencyForSell(store, listing, out var currency, out var unitPrice) || unitPrice <= 0)
            return false;

        InvalidateInventoryCache(user);
        var owned = GetOwned(user, listing.ProductEntity, listing.MatchMode);
        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;

        var maxPossible = Math.Min(owned, maxByRemaining);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        if (!TryTakeProductUnits(user, listing.ProductEntity, actual, listing.MatchMode))
            return false;

        var totalL = (long) unitPrice * actual;
        if (totalL > int.MaxValue)
            return false;

        GiveCurrency(user, currency, (int) totalL);
        InvalidateInventoryCache(user);

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - actual);

        Sawmill.Info($"TrySell: OK {listing.ProductEntity} x{actual} for {unitPrice} {currency} each");
        return true;
    }

    public bool TrySellFromContainer(
        string listingId,
        EntityUid machine,
        NcStoreComponent? store,
        EntityUid user,
        EntityUid container,
        int count = 1
    )
    {
        if (store == null || store.Listings.Count == 0 || count <= 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Sell, listingId),
            out var listing))
            return false;

        if (!TryPickCurrencyForSell(store, listing, out var currency, out var unitPrice) || unitPrice <= 0)
            return false;

        InvalidateInventoryCache(container);

        var owned = GetOwnedInRoot(container, listing.ProductEntity, listing.MatchMode);
        var maxByRemaining = listing.RemainingCount >= 0 ? listing.RemainingCount : int.MaxValue;

        var maxPossible = Math.Min(owned, maxByRemaining);
        if (maxPossible <= 0)
            return false;

        var actual = Math.Min(count, maxPossible);

        if (!TryTakeProductUnitsFromRoot(container, listing.ProductEntity, actual, listing.MatchMode))
            return false;

        var totalL = (long) unitPrice * actual;
        if (totalL > int.MaxValue)
            return false;

        GiveCurrency(user, currency, (int) totalL);
        InvalidateInventoryCache(container);
        InvalidateInventoryCache(user);

        if (listing.RemainingCount > 0)
            listing.RemainingCount = Math.Max(0, listing.RemainingCount - actual);

        Sawmill.Info(
            $"TrySellFromContainer: OK {listing.ProductEntity} x{actual} for {unitPrice} {currency} each (container={ToPrettyString(container)})");
        return true;
    }


    private int GetOwnedInternal(EntityUid root, string productProtoId, PrototypeMatchMode matchMode)
    {
        var total = 0;

        var expectedStackType = GetProductStackType(productProtoId);
        var effective = ResolveMatchMode(productProtoId, matchMode);

        foreach (var ent in EnumerateDeepItemsUnique(root))
        {
            if (IsProtectedFromDirectSale(root, ent))
                continue;

            if (expectedStackType != null &&
                _ents.TryGetComponent(ent, out StackComponent? stack) &&
                stack.StackTypeId == expectedStackType)
            {
                total += Math.Max(stack.Count, 0);
                continue;
            }

            if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is null)
                continue;

            if (effective == PrototypeMatchMode.Descendants)
            {
                if (IsProtoOrDescendant(meta.EntityPrototype, productProtoId))
                    total += 1;
            }
            else
            {
                if (meta.EntityPrototype.ID == productProtoId)
                    total += 1;
            }
        }

        return total;
    }


    public int GetOwned(EntityUid user, string productProtoId) =>
        GetOwnedInternal(user, productProtoId, PrototypeMatchMode.Exact);

    public int GetOwned(EntityUid user, string productProtoId, PrototypeMatchMode matchMode) =>
        GetOwnedInternal(user, productProtoId, matchMode);

    public int GetOwnedInRoot(EntityUid root, string productProtoId) =>
        GetOwnedInternal(root, productProtoId, PrototypeMatchMode.Exact);

    public int GetOwnedInRoot(EntityUid root, string productProtoId, PrototypeMatchMode matchMode) =>
        GetOwnedInternal(root, productProtoId, matchMode);

    private bool TryTakeProductUnitsInternal(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode)
    {
        if (amount <= 0)
            return true;

        InvalidateInventoryCache(root);

        _scratchItems.Clear();
        foreach (var item in EnumerateDeepItemsUnique(root))
            _scratchItems.Add(item);

        var allItems = _scratchItems;
        var stackType = GetProductStackType(protoId);
        var availableTotal = 0;
        var effective = ResolveMatchMode(protoId, matchMode);

        bool Matches(EntityPrototype proto)
        {
            if (effective == PrototypeMatchMode.Exact)
                return proto.ID == protoId;
            return proto.ID == protoId || IsProtoOrDescendant(proto, protoId);
        }

        foreach (var ent in allItems)
        {
            if (!_ents.EntityExists(ent))
                continue;
            if (IsProtectedFromDirectSale(root, ent))
                continue;

            if (stackType != null)
            {
                if (_ents.TryGetComponent(ent, out StackComponent? stack) && stack.StackTypeId == stackType)
                    availableTotal += Math.Max(stack.Count, 0);
            }
            else
            {
                if (_ents.TryGetComponent(ent, out StackComponent? stack))
                {
                    if (_ents.TryGetComponent(ent, out MetaDataComponent? meta) && meta.EntityPrototype != null &&
                        Matches(meta.EntityPrototype))
                        availableTotal += stack.Count;
                }
                else if (_ents.TryGetComponent(ent, out MetaDataComponent? meta) && meta.EntityPrototype != null)
                {
                    if (Matches(meta.EntityPrototype))
                        availableTotal += 1;
                }
            }

            if (availableTotal >= amount)
                break;
        }

        if (availableTotal < amount)
            return false;

        var left = amount;

        foreach (var ent in allItems)
        {
            if (left <= 0)
                break;
            if (!_ents.EntityExists(ent))
                continue;
            if (IsProtectedFromDirectSale(root, ent))
                continue;

            if (stackType != null)
            {
                if (!_ents.TryGetComponent(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
                    continue;

                var have = Math.Max(stack.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, left);
                var newCount = have - take;
                _stacks.SetCount(ent, newCount, stack);

                if (newCount <= 0 && _ents.EntityExists(ent))
                    _ents.DeleteEntity(ent);

                left -= take;
            }
            else
            {
                if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                    continue;

                var matches = effective == PrototypeMatchMode.Exact
                    ? meta.EntityPrototype.ID == protoId
                    : Matches(meta.EntityPrototype);

                if (!matches)
                    continue;
                if (_ents.TryGetComponent(ent, out StackComponent? st))
                {
                    var have = st.Count;
                    var take = Math.Min(have, left);

                    if (take >= have)
                        _ents.DeleteEntity(ent);
                    else
                        _stacks.SetCount(ent, have - take, st);

                    left -= take;
                }
                else
                {
                    _ents.DeleteEntity(ent);
                    left -= 1;
                }
            }
        }

        InvalidateInventoryCache(root);
        return left <= 0;
    }

    private bool TryTakeProductUnitsFromCachedList(
        EntityUid root,
        List<EntityUid> cachedItems,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        if (amount <= 0)
            return true;

        var stackType = GetProductStackType(protoId);
        var availableTotal = 0;

        var effective = ResolveMatchMode(protoId, matchMode);

        bool Matches(EntityPrototype proto)
        {
            if (effective == PrototypeMatchMode.Exact)
                return proto.ID == protoId;
            return proto.ID == protoId || IsProtoOrDescendant(proto, protoId);
        }

        foreach (var ent in cachedItems)
        {
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;
            if (IsProtectedFromDirectSale(root, ent))
                continue;

            if (stackType != null)
            {
                if (_ents.TryGetComponent(ent, out StackComponent? stack) && stack.StackTypeId == stackType)
                    availableTotal += Math.Max(stack.Count, 0);
            }
            else
            {
                if (_ents.TryGetComponent(ent, out MetaDataComponent? meta) && meta.EntityPrototype != null)
                {
                    if (Matches(meta.EntityPrototype))
                        availableTotal += 1;
                }
            }

            if (availableTotal >= amount)
                break;
        }

        if (availableTotal < amount)
            return false;

        var left = amount;

        if (stackType != null)
        {
            for (var i = 0; i < cachedItems.Count && left > 0; i++)
            {
                var ent = cachedItems[i];
                if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                    continue;
                if (IsProtectedFromDirectSale(root, ent))
                    continue;

                if (!_ents.TryGetComponent(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
                    continue;

                var have = Math.Max(stack.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, left);
                var newCount = have - take;
                _stacks.SetCount(ent, newCount, stack);

                if (newCount <= 0 && _ents.EntityExists(ent))
                {
                    _ents.DeleteEntity(ent);
                    cachedItems[i] = EntityUid.Invalid;
                }

                left -= take;
            }
        }
        else
        {
            for (var i = 0; i < cachedItems.Count && left > 0; i++)
            {
                var ent = cachedItems[i];
                if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                    continue;
                if (IsProtectedFromDirectSale(root, ent))
                    continue;
                if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                    continue;

                if (meta.EntityPrototype.ID == protoId)
                    DeleteOrDecrement(i, ent);
            }

            if (left > 0 && effective != PrototypeMatchMode.Exact)
            {
                for (var i = 0; i < cachedItems.Count && left > 0; i++)
                {
                    var ent = cachedItems[i];
                    if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                        continue;
                    if (IsProtectedFromDirectSale(root, ent))
                        continue;
                    if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                        continue;

                    if (meta.EntityPrototype.ID == protoId)
                        continue;

                    if (Matches(meta.EntityPrototype))
                        DeleteOrDecrement(i, ent);
                }
            }

            void DeleteOrDecrement(int index, EntityUid item)
            {
                if (_ents.TryGetComponent(item, out StackComponent? st) && st.Count > 1)
                    _stacks.SetCount(item, st.Count - 1, st);
                else
                {
                    _ents.DeleteEntity(item);
                    cachedItems[index] = EntityUid.Invalid;
                }

                left -= 1;
            }
        }

        return left <= 0;
    }


    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount) =>
        TryTakeProductUnitsInternal(user, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsInternal(user, protoId, amount, matchMode);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount) =>
        TryTakeProductUnitsInternal(root, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsInternal(root, protoId, amount, matchMode);

    public bool TryExchange(
        string listingId,
        EntityUid machine,
        NcStoreComponent? store,
        EntityUid user
    )
    {
        if (store == null || store.Listings.Count == 0)
            return false;

        if (!store.ListingIndex.TryGetValue(
            NcStoreComponent.MakeListingKey(StoreMode.Exchange, listingId),
            out var listing))
            return false;

        if (string.IsNullOrEmpty(listing.ProductEntity))
            return false;

        var requiredCount = listing.RemainingCount > 0
            ? listing.RemainingCount
            : 1;

        if (requiredCount <= 0)
            return false;

        InvalidateInventoryCache(user);
        var owned = GetOwned(user, listing.ProductEntity, listing.MatchMode);
        if (owned < requiredCount)
            return false;

        if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var rewardPerUnit) ||
            rewardPerUnit <= 0)
            return false;

        if (!TryTakeProductUnits(user, listing.ProductEntity, requiredCount, listing.MatchMode))
            return false;

        var totalRewardL = (long) rewardPerUnit * requiredCount;
        if (totalRewardL > int.MaxValue)
            return false;

        GiveCurrency(user, currencyId, (int) totalRewardL);

        InvalidateInventoryCache(user);

        listing.RemainingCount = 0;

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

        _scratchVisited.Clear();
        _scratchQueue.Clear();
        _scratchResult.Clear();

        void Enqueue(EntityUid uid)
        {
            if (!_scratchVisited.Add(uid))
                return;

            _scratchQueue.Enqueue(uid);
            _scratchResult.Add(uid);
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
                if (slot is { HasItem: true, Item: not null, })
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

        while (_scratchQueue.Count > 0)
        {
            var current = _scratchQueue.Dequeue();

            if (_ents.TryGetComponent(current, out ContainerManagerComponent? cmc))
            {
                foreach (var container in cmc.Containers.Values)
                {
                    foreach (var child in container.ContainedEntities)
                        Enqueue(child);
                }
            }
        }

        if (!_inventoryCache.TryGetValue(owner, out var cachedList))
        {
            cachedList = new(_scratchResult.Count);
            _inventoryCache[owner] = cachedList;
        }
        else
            cachedList.Clear();

        cachedList.AddRange(_scratchResult);

        foreach (var ent in cachedList)
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

        if (string.IsNullOrWhiteSpace(stackType))
            return;

        InvalidateInventoryCache(user);

        if (!_protos.TryIndex<StackPrototype>(stackType, out var proto))
            return;

        long remaining = amount;

        foreach (var ent in EnumerateDeepItemsUnique(user))
        {
            if (remaining <= 0)
                break;

            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                continue;

            var maxPerStack = proto.MaxCount ?? int.MaxValue;
            if (maxPerStack <= 0)
                maxPerStack = 1;

            var canAdd = (long) maxPerStack - st.Count;
            if (canAdd <= 0)
                continue;

            var add = Math.Min(canAdd, remaining);

            var newCountL = st.Count + add;
            var newCount = (int) Math.Clamp(newCountL, 0L, maxPerStack);

            _stacks.SetCount(ent, newCount, st);
            remaining -= add;
        }

        if (remaining <= 0)
            return;

        var coords = _ents.GetComponent<TransformComponent>(user).Coordinates;

        var perStackLimit = proto.MaxCount ?? int.MaxValue;
        if (perStackLimit <= 0)
            perStackLimit = 1;
        var spawnGuard = 0;
        const int maxSpawnedStacksPerCall = 256;

        while (remaining > 0 && spawnGuard < maxSpawnedStacksPerCall)
        {
            var addL = Math.Min(remaining, perStackLimit);
            var add = (int) Math.Clamp(addL, 1L, perStackLimit);

            var spawned = _ents.SpawnEntity(proto.Spawn, coords);

            if (_ents.TryGetComponent(spawned, out StackComponent? newStack))
                _stacks.SetCount(spawned, add, newStack);

            if (_ents.HasComponent<HandsComponent>(user))
                _hands.TryPickupAnyHand(user, spawned, false);

            remaining -= add;
            spawnGuard++;
        }

        if (remaining > 0)
        {
            Sawmill.Warning(
                $"[NcStore] GiveCurrency: spawn guard tripped. user={ToPrettyString(user)}, currency={stackType}, remaining={remaining}");
        }

        InvalidateInventoryCache(user);
    }

    private int GetInheritanceDepth(string protoId)
    {
        if (_inheritanceDepthCache.TryGetValue(protoId, out var depth))
            return depth;

        if (!_protos.TryIndex<EntityPrototype>(protoId, out var proto))
        {
            _inheritanceDepthCache[protoId] = 0;
            return 0;
        }

        var max = 0;
        if (proto.Parents != null)
        {
            foreach (var parent in proto.Parents)
            {
                var d = GetInheritanceDepth(parent) + 1;
                if (d > max)
                    max = d;
            }
        }

        _inheritanceDepthCache[protoId] = max;
        return max;
    }

    private static void SafeAddIncome(Dictionary<string, int> income, string currencyId, long delta)
    {
        if (delta <= 0)
            return;

        if (!income.TryGetValue(currencyId, out var cur))
            cur = 0;

        var sum = cur + delta;
        if (sum >= int.MaxValue)
            income[currencyId] = int.MaxValue;
        else
            income[currencyId] = (int) sum;
    }

    public MassSellPlan ComputeMassSellPlan(NcStoreComponent store, EntityUid container)
    {
        var incomeByCurrency = new Dictionary<string, int>();
        var unitsByListingId = new Dictionary<string, int>();
        var priceByListingId = new Dictionary<string, (string, int)>();
        var steps = new List<MassSellStep>();

        if (store.Listings.Count == 0)
            return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);

        InvalidateInventoryCache(container);

        var stackTypeCounts = new Dictionary<string, int>();
        var protoCounts = new Dictionary<string, int>();
        var protoCache = new Dictionary<string, EntityPrototype>();

        foreach (var ent in EnumerateDeepItemsUnique(container))
        {
            if (_ents.TryGetComponent(ent, out StackComponent? st))
            {
                var cnt = Math.Max(st.Count, 0);
                if (cnt > 0 && !string.IsNullOrWhiteSpace(st.StackTypeId))
                {
                    stackTypeCounts.TryGetValue(st.StackTypeId, out var prev);
                    stackTypeCounts[st.StackTypeId] = prev + cnt;
                }

                continue;
            }

            if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype is null)
                continue;

            var proto = meta.EntityPrototype;

            if (!protoCounts.TryAdd(proto.ID, 1))
                protoCounts[proto.ID] += 1;

            protoCache[proto.ID] = proto;
        }

        if (stackTypeCounts.Count == 0 && protoCounts.Count == 0)
            return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);

        var protoIds = protoCounts.Count > 0
            ? protoCounts.Keys
                .OrderByDescending(GetInheritanceDepth)
                .ThenBy(x => x, OrdinalIds)
                .ToArray()
            : [];

        var listingPrices = new Dictionary<string, int>();
        foreach (var l in store.Listings)
        {
            if (l.Mode != StoreMode.Sell)
                continue;
            if (TryPickCurrencyForSell(store, l, out _, out var price))
                listingPrices[l.Id] = price;
            else
                listingPrices[l.Id] = 0;
        }

        var sellListings = store.Listings
            .Where(l =>
                l.Mode == StoreMode.Sell &&
                !string.IsNullOrEmpty(l.ProductEntity) &&
                l.RemainingCount != 0 &&
                listingPrices.TryGetValue(l.Id, out var p) && p > 0)
            .OrderByDescending(l => listingPrices[l.Id]) // Сначала дорогие
            .ThenByDescending(l => GetInheritanceDepth(l.ProductEntity))
            .ThenBy(l => l.ProductEntity, OrdinalIds)
            .ThenBy(l => l.Id, OrdinalIds)
            .ToArray();

        if (sellListings.Length == 0)
            return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);

        var stackName = _compFactory.GetComponentName(typeof(StackComponent));

        foreach (var listing in sellListings)
        {
            if (!TryPickCurrencyForSell(store, listing, out var currencyId, out var unitPrice))
                continue;

            if (unitPrice <= 0 || string.IsNullOrWhiteSpace(currencyId))
                continue;

            var remaining = listing.RemainingCount;
            if (remaining < -1)
                remaining = -1;

            var maxByRemaining = remaining >= 0 ? remaining : int.MaxValue;
            if (maxByRemaining <= 0)
                continue;

            var maxTakeByInt = int.MaxValue / unitPrice;
            if (maxTakeByInt <= 0)
                continue;

            var want = Math.Min(maxByRemaining, maxTakeByInt);

            string? expectedStackType = null;
            if (_protos.TryIndex<EntityPrototype>(listing.ProductEntity, out var prodProto) &&
                prodProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                expectedStackType = prodStackDef.StackTypeId;

            var taken = 0;
            var effectiveMatch = ResolveMatchMode(listing.ProductEntity, listing.MatchMode);

            if (!string.IsNullOrEmpty(expectedStackType))
            {
                if (!stackTypeCounts.TryGetValue(expectedStackType, out var available) || available <= 0)
                    continue;

                taken = Math.Min(available, want);
                stackTypeCounts[expectedStackType] = available - taken;
            }
            else
            {
                if (protoIds.Length == 0)
                    continue;

                if (effectiveMatch != PrototypeMatchMode.Descendants)
                {
                    if (!protoCounts.TryGetValue(listing.ProductEntity, out var available) || available <= 0)
                        continue;

                    taken = Math.Min(available, want);
                    protoCounts[listing.ProductEntity] = available - taken;
                }
                else
                {
                    foreach (var protoId in protoIds)
                    {
                        if (taken >= want)
                            break;

                        if (!protoCounts.TryGetValue(protoId, out var available) || available <= 0)
                            continue;

                        if (!protoCache.TryGetValue(protoId, out var proto) &&
                            !_protos.TryIndex(protoId, out proto))
                            continue;

                        protoCache[protoId] = proto;

                        if (!IsProtoOrDescendant(proto, listing.ProductEntity))
                            continue;

                        var take = Math.Min(available, want - taken);
                        if (take <= 0)
                            continue;

                        protoCounts[protoId] = available - take;
                        taken += take;
                    }
                }
            }

            if (taken <= 0)
                continue;

            var total = (long) unitPrice * taken;
            SafeAddIncome(incomeByCurrency, currencyId, total);

            unitsByListingId[listing.Id] = taken;
            priceByListingId[listing.Id] = (currencyId, unitPrice);
            steps.Add(new(listing, currencyId, unitPrice, taken));
        }

        return new(incomeByCurrency, unitsByListingId, priceByListingId, steps);
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

        _scratchItems.Clear();
        foreach (var item in EnumerateDeepItemsUnique(container))
            _scratchItems.Add(item);

        var cachedItems = _scratchItems;

        var plan = ComputeMassSellPlan(store, container);
        if (plan.Steps.Count == 0 || plan.IncomeByCurrency.Count == 0)
            return false;

        var incomeActual = new Dictionary<string, int>();
        var any = false;

        foreach (var step in plan.Steps)
        {
            if (step.Count <= 0 ||
                step.UnitPrice <= 0 ||
                string.IsNullOrWhiteSpace(step.CurrencyId) ||
                string.IsNullOrWhiteSpace(step.Listing.ProductEntity))
                continue;

            var listing = step.Listing;

            var remaining = listing.RemainingCount;
            if (remaining < -1)
                remaining = -1;

            var maxByRemaining = remaining >= 0 ? remaining : int.MaxValue;
            if (maxByRemaining <= 0)
                continue;

            var take = Math.Min(step.Count, maxByRemaining);
            if (take <= 0)
                continue;

            if (!TryTakeProductUnitsFromCachedList(
                container,
                cachedItems,
                listing.ProductEntity,
                take,
                listing.MatchMode))
                continue;

            if (listing.RemainingCount > 0)
                listing.RemainingCount = Math.Max(0, listing.RemainingCount - take);

            var total = (long) step.UnitPrice * take;
            SafeAddIncome(incomeActual, step.CurrencyId, total);

            any = true;
        }

        if (!any || incomeActual.Count == 0)
            return false;

        foreach (var (currency, amount) in incomeActual)
        {
            if (amount <= 0)
                continue;

            GiveCurrency(user, currency, amount);
        }

        InvalidateInventoryCache(container);
        InvalidateInventoryCache(user);

        return true;
    }

    private bool IsHeldInHands(EntityUid user, EntityUid item)
    {
        if (!_ents.TryGetComponent(user, out HandsComponent? hands))
            return false;

        foreach (var hand in hands.Hands.Values)
            if (hand.HeldEntity == item)
                return true;

        return false;
    }

    private bool IsDirectChildOf(EntityUid root, EntityUid item) =>
        _ents.TryGetComponent(item, out TransformComponent? xform) && xform.ParentUid == root;

    private bool IsProtectedFromDirectSale(EntityUid root, EntityUid item)
    {
        if (!_ents.HasComponent<InventoryComponent>(root))
            return false;

        if (!IsDirectChildOf(root, item))
            return false;

        if (IsHeldInHands(root, item))
            return false;

        return _ents.HasComponent<ClothingComponent>(item);
    }


    public Dictionary<string, int> GetMassSellValue(
        NcStoreComponent store,
        EntityUid container
    ) =>
        ComputeMassSellPlan(store, container).IncomeByCurrency;


    public bool TrySpawnProduct(string protoId, EntityUid user)
    {
        try
        {
            var userCoords = _ents.GetComponent<TransformComponent>(user).Coordinates;
            var spawned = _ents.SpawnEntity(protoId, userCoords);

            var pickedUp = false;
            if (_ents.HasComponent<HandsComponent>(user))
                pickedUp = _hands.TryPickupAnyHand(user, spawned, false);

            if (!pickedUp && TryGetPulledClosedCrate(user, out var crate) && Exists(crate))
            {
                _entityStorage.Insert(spawned, crate);
                InvalidateInventoryCache(crate);
            }

            InvalidateInventoryCache(user);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error($"Spawn failed for {protoId}: {e}");
            return false;
        }
    }

    public bool ExecuteContractBatch(Dictionary<(EntityUid Root, string ProtoId), int> plan)
    {
        foreach (var ((root, protoId), amount) in plan)
        {
            if (amount <= 0)
                continue;
            var available = GetOwnedInRoot(root, protoId, PrototypeMatchMode.Exact);

            if (available < amount)
            {
                Sawmill.Warning(
                    $"[NcStore] ExecuteContractBatch dry-run failed: {ToPrettyString(root)} has {available} of {protoId}, needed {amount}. Aborting transaction.");
                return false;
            }
        }

        var rootsToInvalidate = new HashSet<EntityUid>();
        var success = true;

        foreach (var ((root, protoId), amount) in plan)
        {
            if (amount <= 0)
                continue;

            rootsToInvalidate.Add(root);
            if (!TryTakeProductUnitsUnsafe(root, protoId, amount, PrototypeMatchMode.Exact))
            {
                Sawmill.Error(
                    $"[NcStore] ExecuteContractBatch CRITICAL: Validation passed but take failed for {amount} of {protoId} from {ToPrettyString(root)}. Transaction interrupted partially.");
                success = false;
                break;
            }
        }

        foreach (var root in rootsToInvalidate)
            InvalidateInventoryCache(root);

        return success;
    }

    private bool TryTakeProductUnitsUnsafe(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode)
    {
        if (!_inventoryCache.TryGetValue(root, out var cachedItems))
        {
            var _ = EnumerateDeepItemsUnique(root).FirstOrDefault();

            if (!_inventoryCache.TryGetValue(root, out cachedItems))
                return false;
        }

        var stackType = GetProductStackType(protoId);
        var effective = ResolveMatchMode(protoId, matchMode);

        bool Matches(EntityPrototype proto)
        {
            if (effective == PrototypeMatchMode.Exact)
                return proto.ID == protoId;
            return proto.ID == protoId || IsProtoOrDescendant(proto, protoId);
        }

        var left = amount;

        for (var i = 0; i < cachedItems.Count && left > 0; i++)
        {
            var ent = cachedItems[i];

            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;

            if (IsProtectedFromDirectSale(root, ent))
                continue;

            if (stackType != null)
            {
                if (!_ents.TryGetComponent(ent, out StackComponent? stack) || stack.StackTypeId != stackType)
                    continue;

                var have = Math.Max(stack.Count, 0);
                if (have <= 0)
                    continue;

                var take = Math.Min(have, left);
                var newCount = have - take;

                _stacks.SetCount(ent, newCount, stack);

                if (newCount <= 0 && _ents.EntityExists(ent))
                    _ents.DeleteEntity(ent);

                left -= take;
            }
            else
            {
                if (!_ents.TryGetComponent(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
                    continue;

                if (!Matches(meta.EntityPrototype))
                    continue;

                if (_ents.TryGetComponent(ent, out StackComponent? st) && st.Count > 1)
                    _stacks.SetCount(ent, st.Count - 1, st);
                else
                    _ents.DeleteEntity(ent);

                left -= 1;
            }
        }

        return left <= 0;
    }

    private sealed class OrdinalIdComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) => string.CompareOrdinal(x, y);
    }


    public readonly record struct MassSellStep(
        StoreListingPrototype Listing,
        string CurrencyId,
        int UnitPrice,
        int Count);

    public readonly record struct MassSellPlan(
        Dictionary<string, int> IncomeByCurrency,
        Dictionary<string, int> UnitsByListingId,
        Dictionary<string, (string CurrencyId, int UnitPrice)> PriceByListingId,
        List<MassSellStep> Steps);

    public sealed class InventorySnapshot
    {
        public readonly Dictionary<string, int> AncestorCounts = new();
        public readonly Dictionary<string, int> ProtoCounts = new();
        public readonly Dictionary<string, int> StackTypeCounts = new();

        public void Clear()
        {
            ProtoCounts.Clear();
            AncestorCounts.Clear();
            StackTypeCounts.Clear();
        }
    }
}
