using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
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

            if (best == null || OrdinalIds.Compare(kv.Key, best.Value.Key) < 0)
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

                if (!listing.Cost.TryGetValue(cur, out var p))
                    continue;

                if (p <= 0)
                    continue;

                currency = cur;
                price = p;
                return true;
            }

            return false;
        }

        KeyValuePair<string, int>? best = null;
        foreach (var kv in listing.Cost)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0)
                continue;

            if (best == null || OrdinalIds.Compare(kv.Key, best.Value.Key) < 0)
                best = kv;
        }

        if (best == null)
            return false;

        currency = best.Value.Key;
        price = best.Value.Value;
        return true;
    }
}
