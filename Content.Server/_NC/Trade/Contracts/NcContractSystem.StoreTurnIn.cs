using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void ScanStoreNearbyTurnInItems(EntityUid store, List<EntityUid> itemsBuffer)
    {
        itemsBuffer.Clear();

        foreach (var ent in _lookup.GetEntitiesInRange(store, NcContractTuning.TrackedDeliveryStoreRange, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (ent == EntityUid.Invalid || ent == store || !EntityManager.EntityExists(ent))
                continue;

            if (!TryComp(ent, out TransformComponent? xform) || IsTargetInEntityContainer(xform))
                continue;

            if (!CanUseNearbyStoreTurnInEntity(ent, xform))
                continue;

            itemsBuffer.Add(ent);
        }
    }

    private bool CanUseNearbyStoreTurnInEntity(EntityUid ent, TransformComponent xform)
    {
        if (HasComp<ItemComponent>(ent))
            return true;

        // Allow non-item movable world objects (e.g. placeable structures like pianos)
        // while excluding mobs and anchored world geometry/decor.
        if (HasComp<MobStateComponent>(ent))
            return false;

        if (xform.Anchored)
            return false;

        return HasComp<PullableComponent>(ent);
    }
}
