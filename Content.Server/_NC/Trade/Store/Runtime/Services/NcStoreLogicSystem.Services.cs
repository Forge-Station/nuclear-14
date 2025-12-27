using Content.Shared._NC.Trade;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private StoreCurrencyService _currencyService = default!;
    private StoreSpawnService _spawnService = default!;

    private void InitializeServices()
    {
        _currencyService = new(this);
        _spawnService = new(this);
    }

    private sealed class StoreCurrencyService
    {
        private readonly NcStoreLogicSystem _sys;
        public StoreCurrencyService(NcStoreLogicSystem sys) { _sys = sys; }

        public bool TryPickCurrencyForBuy(
            NcStoreComponent store,
            StoreListingPrototype listing,
            in NcInventorySnapshot snapshot,
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
            var hasWhitelist = false;
            foreach (var c in store.CurrencyWhitelist)
                if (!string.IsNullOrWhiteSpace(c))
                {
                    hasWhitelist = true;
                    break;
                }

            if (hasWhitelist)
            {
                foreach (var cur in store.CurrencyWhitelist)
                {
                    if (string.IsNullOrWhiteSpace(cur))
                        continue;
                    if (!listing.Cost.TryGetValue(cur, out var price))
                        continue;
                    if (price <= 0)
                        continue;
                    var bal = snapshot.StackTypeCounts.TryGetValue(cur, out var b) ? b : 0;
                    if (bal < price)
                        continue;
                    currency = cur;
                    unitPrice = price;
                    balance = bal;
                    return true;
                }

                return false;
            }

            KeyValuePair<string, int>? best = null;
            foreach (var kv in listing.Cost)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0)
                    continue;
                if (best == null || string.CompareOrdinal(kv.Key, best.Value.Key) < 0)
                    best = kv;
            }

            if (best == null)
                return false;
            var fallbackCur = best.Value.Key;
            var fallbackPrice = best.Value.Value;
            var fallbackBal = snapshot.StackTypeCounts.TryGetValue(fallbackCur, out var fb) ? fb : 0;
            if (fallbackBal < fallbackPrice)
                return false;
            currency = fallbackCur;
            unitPrice = fallbackPrice;
            balance = fallbackBal;
            return true;
        }

        public bool TryTakeCurrency(EntityUid user, string stackType, int amount)
        {
            if (amount <= 0)
                return true;

            // Используем новую систему инвентаря для получения предметов
            var cachedItems = _sys._inventory.GetOrBuildDeepItemsCache(user);

            _sys._scratchCurrencyCandidates.Clear();
            var total = 0;
            for (var i = 0; i < cachedItems.Count; i++)
            {
                var ent = cachedItems[i];
                if (ent == EntityUid.Invalid || !_sys._ents.EntityExists(ent))
                    continue;
                if (!_sys._ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                    continue;
                var cnt = Math.Max(st.Count, 0);
                if (cnt <= 0)
                    continue;
                _sys._scratchCurrencyCandidates.Add((ent, cnt));
                total += cnt;
            }

            if (total < amount)
                return false;
            _sys._scratchCurrencyCandidates.Sort((a, b) => a.Count.CompareTo(b.Count));

            var left = amount;
            foreach (var (ent, have) in _sys._scratchCurrencyCandidates)
            {
                if (left <= 0)
                    break;
                var take = Math.Min(have, left);
                if (_sys._ents.TryGetComponent(ent, out StackComponent? st))
                {
                    _sys._stacks.SetCount(ent, st.Count - take, st);
                    if (st.Count <= 0 && _sys._ents.EntityExists(ent))
                        _sys._ents.DeleteEntity(ent);
                }

                left -= take;
            }

            return left <= 0;
        }

        public void GiveCurrency(EntityUid user, string stackType, int amount)
        {
            if (amount <= 0 || string.IsNullOrWhiteSpace(stackType))
                return;

            _sys._inventory.InvalidateInventoryCache(user);
            if (!_sys._protos.TryIndex<StackPrototype>(stackType, out var proto))
                return;

            long remaining = amount;
            var cached = _sys._inventory.GetOrBuildDeepItemsCacheCompacted(user);

            foreach (var ent in cached)
            {
                if (remaining <= 0)
                    break;
                if (!_sys._ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                    continue;
                var maxPerStack = proto.MaxCount ?? int.MaxValue;
                if (maxPerStack <= 0)
                    maxPerStack = 1;
                var canAdd = (long) maxPerStack - st.Count;
                if (canAdd <= 0)
                    continue;
                var add = Math.Min(canAdd, remaining);
                var newCount = (int) Math.Clamp(st.Count + add, 0L, maxPerStack);
                _sys._stacks.SetCount(ent, newCount, st);
                remaining -= add;
            }

            if (remaining <= 0)
                return;
            var coords = _sys._ents.GetComponent<TransformComponent>(user).Coordinates;
            var perStackLimit = proto.MaxCount ?? int.MaxValue;
            if (perStackLimit <= 0)
                perStackLimit = 1;

            while (remaining > 0)
            {
                var addL = Math.Min(remaining, perStackLimit);
                var add = (int) Math.Clamp(addL, 1L, perStackLimit);
                var spawned = _sys._ents.SpawnEntity(proto.Spawn, coords);
                if (_sys._ents.TryGetComponent(spawned, out StackComponent? newStack))
                    _sys._stacks.SetCount(spawned, add, newStack);
                if (_sys._ents.HasComponent<HandsComponent>(user))
                    _sys._hands.TryPickupAnyHand(user, spawned, false);
                remaining -= add;
            }

            _sys._inventory.InvalidateInventoryCache(user);
        }
    }

    private sealed class StoreSpawnService
    {
        private readonly NcStoreLogicSystem _sys;
        public StoreSpawnService(NcStoreLogicSystem sys) { _sys = sys; }

        public int SpawnPurchasedProduct(
            EntityUid user,
            string productEntity,
            EntityPrototype productProto,
            int amount,
            int unitPrice,
            string currency
        )
        {
            if (amount <= 0)
                return 0;
            var spawnedTotal = 0;
            if (productProto.TryGetComponent("Stack", out StackComponent? stackComp))
            {
                var userCoords = _sys._ents.GetComponent<TransformComponent>(user).Coordinates;
                var maxPerStack = int.MaxValue;
                if (!string.IsNullOrWhiteSpace(stackComp.StackTypeId) &&
                    _sys._protos.TryIndex<StackPrototype>(stackComp.StackTypeId, out var stackTypeProto))
                    maxPerStack = stackTypeProto.MaxCount ?? int.MaxValue;
                if (maxPerStack <= 0)
                    maxPerStack = 1;

                var remainingToSpawn = amount;
                while (remainingToSpawn > 0)
                {
                    var chunk = Math.Min(remainingToSpawn, maxPerStack);
                    try
                    {
                        var spawned = _sys._ents.SpawnEntity(productEntity, userCoords);
                        if (_sys._ents.TryGetComponent(spawned, out StackComponent? spawnedStack))
                            _sys._stacks.SetCount(spawned, chunk, spawnedStack);

                        var pickedUp = false;
                        if (_sys._ents.HasComponent<HandsComponent>(user))
                            pickedUp = _sys._hands.TryPickupAnyHand(user, spawned, false);

                        if (!pickedUp && _sys.TryGetPulledClosedCrate(user, out var crate) && _sys.Exists(crate))
                        {
                            _sys._entityStorage.Insert(spawned, crate);
                            _sys._inventory.InvalidateInventoryCache(crate);
                        }

                        spawnedTotal += chunk;
                        remainingToSpawn -= chunk;
                    }
                    catch (Exception e)
                    {
                        Sawmill.Error($"Spawn failed during bulk buy: {e}");
                        if (remainingToSpawn > 0)
                        {
                            var refundL = (long) remainingToSpawn * unitPrice;
                            if (refundL > 0 && refundL <= int.MaxValue)
                                _sys.GiveCurrency(user, currency, (int) refundL);
                        }

                        break;
                    }
                }

                return spawnedTotal;
            }

            for (var i = 0; i < amount; i++)
                if (_sys.TrySpawnProduct(productEntity, user))
                    spawnedTotal++;
                else
                    _sys.GiveCurrency(user, currency, unitPrice);
            return spawnedTotal;
        }
    }
}
