using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{

    private bool TryTakeCurrency(EntityUid user, string stackType, int amount)
    {
        if (amount <= 0)
            return true;

        var cachedItems = GetOrBuildDeepItemsCache(user);
        _scratchCurrencyCandidates.Clear();

        var total = 0;
        for (var i = 0; i < cachedItems.Count; i++)
        {
            var ent = cachedItems[i];
            if (ent == EntityUid.Invalid || !_ents.EntityExists(ent))
                continue;

            if (!_ents.TryGetComponent(ent, out StackComponent? st) || st.StackTypeId != stackType)
                continue;

            var cnt = Math.Max(st.Count, 0);
            if (cnt <= 0)
                continue;

            _scratchCurrencyCandidates.Add((ent, cnt));
            total += cnt;
        }

        if (total < amount)
            return false;

        _scratchCurrencyCandidates.Sort(static (a, b) => a.Count.CompareTo(b.Count));

        var left = amount;
        foreach (var (ent, have) in _scratchCurrencyCandidates)
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

        var cached = GetOrBuildDeepItemsCache(user);
        CompactCachedItems(cached);

        foreach (var ent in cached)
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
        while (remaining > 0)
        {
            var addL = Math.Min(remaining, perStackLimit);
            var add = (int) Math.Clamp(addL, 1L, perStackLimit);

            var spawned = _ents.SpawnEntity(proto.Spawn, coords);

            if (_ents.TryGetComponent(spawned, out StackComponent? newStack))
                _stacks.SetCount(spawned, add, newStack);

            if (_ents.HasComponent<HandsComponent>(user))
                _hands.TryPickupAnyHand(user, spawned, false);

            remaining -= add;
        }

        InvalidateInventoryCache(user);
    }
}
