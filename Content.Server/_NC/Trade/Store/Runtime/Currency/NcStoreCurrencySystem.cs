using System.Linq;
using Content.Shared._NC.Trade;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed class NcStoreCurrencySystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    private readonly List<(EntityUid Ent, int Count)> _scratchCandidates = new();
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public bool TryPickCurrencyForBuy(
        NcStoreComponent store,
        NcStoreListingDef listing,
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

    public bool TryPickCurrencyForSell(
        NcStoreComponent store,
        NcStoreListingDef listing,
        out string currency,
        out int unitPrice
    )
    {
        currency = string.Empty;
        unitPrice = 0;
        if (listing.Cost.Count == 0)
            return false;

        foreach (var cur in store.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;
            if (listing.Cost.TryGetValue(cur, out var price) && price > 0)
            {
                currency = cur;
                unitPrice = price;
                return true;
            }
        }

        var first = listing.Cost.FirstOrDefault();
        if (!string.IsNullOrEmpty(first.Key) && first.Value > 0)
        {
            currency = first.Key;
            unitPrice = first.Value;
            return true;
        }

        return false;
    }

    public bool TryTakeCurrency(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0)
            return true;

        var cachedItems = _inventory.GetOrBuildDeepItemsCache(user);

        _scratchCandidates.Clear();
        var total = 0;

        foreach (var ent in cachedItems)
        {
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;
            if (_inventory.IsProtectedFromDirectSale(user, ent))
                continue;

            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                continue;

            var cnt = Math.Max(st.Count, 0);
            if (cnt <= 0)
                continue;

            _scratchCandidates.Add((ent, cnt));
            total += cnt;
        }

        if (total < amount)
            return false;

        _scratchCandidates.Sort((a, b) => a.Count.CompareTo(b.Count));

        var left = amount;
        foreach (var (ent, have) in _scratchCandidates)
        {
            if (left <= 0)
                break;
            var take = Math.Min(have, left);

            if (_ents.TryGetComponent(ent, out StackComponent? st))
            {
                _stacks.SetCount(ent, st.Count - take, st);
                if (st.Count <= 0)
                    _ents.DeleteEntity(ent);
            }

            left -= take;
        }

        if (left <= 0)
        {
            _inventory.InvalidateInventoryCache(user);
            return true;
        }

        return false;
    }

    public void GiveCurrency(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(stackType))
            return;

        _inventory.InvalidateInventoryCache(user);

        if (!_protos.TryIndex<StackPrototype>(stackType, out var proto))
            return;

        var maxPerStack = proto.MaxCount ?? int.MaxValue;
        if (maxPerStack <= 0)
            maxPerStack = 1;

        long remaining = amount;

        var cached = _inventory.GetOrBuildDeepItemsCacheCompacted(user);
        foreach (var ent in cached)
        {
            if (remaining <= 0)
                break;
            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                continue;

            var canAdd = (long) maxPerStack - st.Count;
            if (canAdd <= 0)
                continue;

            var add = Math.Min(canAdd, remaining);
            var newCount = (int) Math.Clamp(st.Count + add, 0L, maxPerStack);

            _stacks.SetCount(ent, newCount, st);
            remaining -= add;
        }

        if (remaining <= 0)
        {
            _inventory.InvalidateInventoryCache(user);
            return;
        }

        var coords = _xform.GetMoverCoordinates(user);

        while (remaining > 0)
        {
            var addL = Math.Min(remaining, maxPerStack);
            var add = (int) Math.Clamp(addL, 1L, maxPerStack);

            var spawned = _ents.SpawnEntity(proto.Spawn, coords);

            if (_ents.TryGetComponent(spawned, out StackComponent? newStack))
                _stacks.SetCount(spawned, add, newStack);

            _hands.TryPickupAnyHand(user, spawned, false);
            remaining -= add;
        }

        _inventory.InvalidateInventoryCache(user);
    }
}
