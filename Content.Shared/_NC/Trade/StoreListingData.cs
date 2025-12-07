using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class StoreListingData
{
    public string Category = string.Empty;
    public string CurrencyId = string.Empty;
    public string Id = string.Empty;
    public StoreMode Mode;
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
        int remaining = -1
    )
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

public enum StoreMode
{
    Buy,
    Sell,
    Exchange
}
