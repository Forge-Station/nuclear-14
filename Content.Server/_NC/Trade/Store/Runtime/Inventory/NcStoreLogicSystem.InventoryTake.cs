using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    /// <summary>
    ///     Atomic consume: first validates availability, then performs entity mutations.
    ///     Uses the per-root deep-items cache to avoid repeated traversals.
    /// </summary>
    private bool TryTakeProductUnitsFromRootCached(
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

    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount) =>
        TryTakeProductUnitsFromRootCached(user, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnits(EntityUid user, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsFromRootCached(user, protoId, amount, matchMode);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount) =>
        TryTakeProductUnitsFromRootCached(root, protoId, amount, PrototypeMatchMode.Exact);

    public bool TryTakeProductUnitsFromRoot(EntityUid root, string protoId, int amount, PrototypeMatchMode matchMode) =>
        TryTakeProductUnitsFromRootCached(root, protoId, amount, matchMode);

}
