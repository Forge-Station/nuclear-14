using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void ScanStoreNearbyTurnInItems(EntityUid store, List<EntityUid> itemsBuffer)
    {
        itemsBuffer.Clear();

        foreach (var ent in _lookup.GetEntitiesInRange(
            store,
            NcContractTuning.TrackedDeliveryStoreRange,
            LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (ent == EntityUid.Invalid || ent == store || !Exists(ent))
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

        if (TryComp(ent, out MobStateComponent? mobState))
        {
            return mobState.CurrentState == MobState.Dead &&
                !xform.Anchored &&
                HasComp<PullableComponent>(ent);
        }

        if (xform.Anchored)
            return false;

        return HasComp<PullableComponent>(ent);
    }
}
