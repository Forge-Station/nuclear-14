using Content.Shared._NC.Trade;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed class NcStoreCurrencySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protos = default!;

    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly List<ICurrencyHandler> _handlers = new();
    private readonly Dictionary<string, ICurrencyHandler> _handlerCache = new(StringComparer.Ordinal);

    public override void Initialize()
    {
        base.Initialize();
        _handlers.Clear();
        _handlerCache.Clear();
        _handlers.Add(new StackCurrencyHandler(_ents, _hands, _inventory, _protos, _stacks, _xform));
    }

    private bool TryResolveHandler(string currencyId, out ICurrencyHandler handler)
    {
        if (_handlerCache.TryGetValue(currencyId, out var cached))
        {
            handler = cached;
            return true;
        }

        foreach (var h in _handlers)
        {
            if (!h.CanHandle(currencyId))
                continue;

            _handlerCache[currencyId] = h;
            handler = h;
            return true;
        }

        handler = default!;
        return false;
    }


    public bool TryGetBalance(in NcInventorySnapshot snapshot, string currencyId, out int balance)
    {
        balance = 0;
        if (!TryResolveHandler(currencyId, out var h))
            return false;
        return h.TryGetBalance(snapshot, currencyId, out balance);
    }

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

        if (HasWhitelistedCurrency(store))
            return TryPickWhitelistedBuyCurrency(store, listing, snapshot, out currency, out unitPrice, out balance);

        return TryPickFallbackBuyCurrency(listing, snapshot, out currency, out unitPrice, out balance);
    }

    private static bool HasWhitelistedCurrency(NcStoreComponent store)
    {
        foreach (var currencyId in store.CurrencyWhitelist)
            if (!string.IsNullOrWhiteSpace(currencyId))
                return true;

        return false;
    }

    private bool TryPickWhitelistedBuyCurrency(
        NcStoreComponent store,
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance)
    {
        currency = string.Empty;
        unitPrice = 0;
        balance = 0;

        foreach (var currencyId in store.CurrencyWhitelist)
        {
            if (!TryGetAffordableBuyCurrency(snapshot, listing, currencyId, out var price, out var currentBalance))
                continue;

            currency = currencyId;
            unitPrice = price;
            balance = currentBalance;
            return true;
        }

        return false;
    }

    private bool TryPickFallbackBuyCurrency(
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance)
    {
        currency = string.Empty;
        unitPrice = 0;
        balance = 0;

        if (!TryGetBestBuyCurrency(listing, out var best))
            return false;

        if (!TryGetBalance(snapshot, best.Key, out balance))
            balance = 0;

        if (balance < best.Value)
            return false;

        currency = best.Key;
        unitPrice = best.Value;
        return true;
    }

    private bool TryGetAffordableBuyCurrency(
        in NcInventorySnapshot snapshot,
        NcStoreListingDef listing,
        string currencyId,
        out int price,
        out int balance)
    {
        price = 0;
        balance = 0;

        if (string.IsNullOrWhiteSpace(currencyId))
            return false;

        if (!listing.Cost.TryGetValue(currencyId, out price) || price <= 0)
            return false;

        if (!TryGetBalance(snapshot, currencyId, out balance))
            balance = 0;

        return balance >= price;
    }

    private static bool TryGetBestBuyCurrency(
        NcStoreListingDef listing,
        out KeyValuePair<string, int> best)
    {
        best = default;
        var found = false;

        foreach (var candidate in listing.Cost)
        {
            if (string.IsNullOrWhiteSpace(candidate.Key) || candidate.Value <= 0)
                continue;

            if (!found || string.CompareOrdinal(candidate.Key, best.Key) < 0)
            {
                best = candidate;
                found = true;
            }
        }

        return found;
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
            if (listing.Cost.TryGetValue(cur, out var price) && price > 0 && TryResolveHandler(cur, out _))
            {
                currency = cur;
                unitPrice = price;
                return true;
            }
        }

        KeyValuePair<string, int>? best = null;
        foreach (var kv in listing.Cost)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0)
                continue;

            if (!TryResolveHandler(kv.Key, out _))
                continue;

            if (best == null || string.CompareOrdinal(kv.Key, best.Value.Key) < 0)
                best = kv;
        }

        if (best == null)
            return false;

        currency = best.Value.Key;
        unitPrice = best.Value.Value;
        return true;
    }

    public bool TryTakeCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;
        if (!TryResolveHandler(currencyId, out var h))
            return false;
        return h.TryTake(user, currencyId, amount);
    }

    public void GiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return;
        if (!TryResolveHandler(currencyId, out var h))
            return;
        h.TryGiveCurrency(user, currencyId, amount);
    }
}
