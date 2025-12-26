using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Robust.Shared.Containers;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem
{
    private IEnumerable<EntityUid> EnumerateDeepItemsUnique(EntityUid owner)
    {
        if (_inventoryCache.TryGetValue(owner, out var cached))
        {
            foreach (var ent in cached)
                if (_ents.EntityExists(ent))
                    yield return ent;

            yield break;
        }

        _scratchVisited.Clear();
        _scratchQueue.Clear();
        _scratchResult.Clear();

        void Enqueue(EntityUid uid)
        {
            if (!_scratchVisited.Add(uid))
                return;

            _scratchQueue.Enqueue(uid);
            _scratchResult.Add(uid);
        }

        if (_ents.TryGetComponent(owner, out InventoryComponent? inventory))
        {
            var slotEnum = new InventorySystem.InventorySlotEnumerator(inventory);
            while (slotEnum.NextItem(out var item))
                Enqueue(item);
        }

        if (_ents.TryGetComponent(owner, out ItemSlotsComponent? itemSlots))
        {
            foreach (var slot in itemSlots.Slots.Values)
                if (slot is { HasItem: true, Item: not null, })
                    Enqueue(slot.Item.Value);
        }

        if (_ents.TryGetComponent(owner, out HandsComponent? hands))
        {
            foreach (var hand in hands.Hands.Values)
                if (hand.HeldEntity.HasValue)
                    Enqueue(hand.HeldEntity.Value);
        }

        if (_ents.TryGetComponent(owner, out ContainerManagerComponent? cmcRoot))
        {
            foreach (var container in cmcRoot.Containers.Values)
            {
                foreach (var entity in container.ContainedEntities)
                    Enqueue(entity);
            }
        }

        while (_scratchQueue.Count > 0)
        {
            var current = _scratchQueue.Dequeue();

            if (_ents.TryGetComponent(current, out ContainerManagerComponent? cmc))
            {
                foreach (var container in cmc.Containers.Values)
                {
                    foreach (var child in container.ContainedEntities)
                        Enqueue(child);
                }
            }
        }

        if (!_inventoryCache.TryGetValue(owner, out var cachedList))
        {
            cachedList = new(_scratchResult.Count);
            _inventoryCache[owner] = cachedList;
        }
        else
            cachedList.Clear();

        cachedList.AddRange(_scratchResult);

        foreach (var ent in cachedList)
            if (_ents.EntityExists(ent))
                yield return ent;
    }

    private bool IsHeldInHands(EntityUid user, EntityUid item)
    {
        if (!_ents.TryGetComponent(user, out HandsComponent? hands))
            return false;

        foreach (var hand in hands.Hands.Values)
            if (hand.HeldEntity == item)
                return true;

        return false;
    }

    private bool IsProtectedFromDirectSale(EntityUid root, EntityUid item)
    {
        if (!_ents.HasComponent<InventoryComponent>(root))
            return false;

        if (!IsDirectChildOf(root, item))
            return false;

        if (IsHeldInHands(root, item))
            return false;

        return _ents.HasComponent<ClothingComponent>(item);
    }

    private bool IsDirectChildOf(EntityUid root, EntityUid item) =>
        _ents.TryGetComponent(item, out TransformComponent? xform) && xform.ParentUid == root;
}
