using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public bool TryTakeProductUnitsFromRootCached(
        EntityUid root,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        return _itemConsumerService.TryTakeProductUnitsFromRootCached(root, protoId, amount, matchMode);
    }
    private bool TryTakeProductUnitsFromCachedList(
        EntityUid root,
        List<EntityUid> cachedItems,
        string protoId,
        int amount,
        PrototypeMatchMode matchMode
    )
    {
        return _itemConsumerService.TryTakeProductUnitsFromCachedList(root, cachedItems, protoId, amount, matchMode);
    }
}
