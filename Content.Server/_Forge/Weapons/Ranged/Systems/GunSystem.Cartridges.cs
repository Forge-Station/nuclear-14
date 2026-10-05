// Adapted from Misfit-Sanctuary/nuclear-14: send cosmetic casing ejections and remove spent server entities.
// Forge adaptation: defer deletion because shooting still dirties the cartridge after ejection.
using Content.Shared._Forge.Weapons.Ranged.Events;
using Robust.Shared.Player;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    protected override void EjectSpentCartridge(EntityUid entity, bool playSound)
    {
        if (TerminatingOrDeleted(entity) || EntityManager.IsQueuedForDeletion(entity))
            return;

        var coordinates = Transform(entity).Coordinates;
        if (MetaData(entity).EntityPrototype is { } prototype)
        {
            RaiseNetworkEvent(new SpentCartridgeEvent(GetNetCoordinates(coordinates), prototype.ID, playSound),
                Filter.Pvs(coordinates, entityMan: EntityManager));
        }

        // Defer deletion: shooting still dirties the cartridge after ejection.
        QueueDel(entity);
    }
}
