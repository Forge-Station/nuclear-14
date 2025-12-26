using System;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

/// <summary>
/// Client-side listing view model (static + dynamic fields).
/// Constructed on the client from <see cref="StoreListingStaticData"/> and <see cref="StoreDynamicState"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class StoreListingData
{
    public string Category = string.Empty;
    public string CurrencyId = string.Empty;
    public string Id = string.Empty;
    public StoreMode Mode;

    // Dynamic
    public int Owned;
    public int Price;
    public string ProductEntity = string.Empty;
    public int Remaining = -1;

    public StoreListingData() { }

    public StoreListingData(
        string id,
        string productEntity,
        int price,
        string category,
        string currencyId,
        StoreMode mode,
        int owned = 0,
        int remaining = -1)
    {
        Id = id;
        ProductEntity = productEntity;
        Price = price;
        Category = category;
        CurrencyId = currencyId;
        Mode = mode;
        Owned = owned;
        Remaining = remaining;
    }
}
