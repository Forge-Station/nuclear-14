using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Warfront.FactionShop;

[Serializable, NetSerializable]
public sealed class FactionShopBuyListingMessage : BoundUserInterfaceMessage
{
    public EntProtoId Product;

    public FactionShopBuyListingMessage(EntProtoId product)
    {
        Product = product;
    }
}
