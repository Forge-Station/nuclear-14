using Content.Shared.Item;
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

            if (!CanUseNearbyStoreTurnInEntity(ent))
                continue;

            itemsBuffer.Add(ent);
        }
    }

    private bool CanUseNearbyStoreTurnInEntity(EntityUid ent)
    {
        if (HasComp<ItemComponent>(ent))
            return false;

        return !TryComp(ent, out MobStateComponent? mobState) || mobState.CurrentState == MobState.Dead;
    }
}
