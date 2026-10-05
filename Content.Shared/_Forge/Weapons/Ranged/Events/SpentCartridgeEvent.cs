using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Weapons.Ranged.Events;

/// <summary>
/// A cosmetic casing ejection sent by the server to clients in PVS range.
/// </summary>
[Serializable, NetSerializable]
public sealed class SpentCartridgeEvent(NetCoordinates coordinates, string prototype, bool playSound) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
    public readonly string Prototype = prototype;
    public readonly bool PlaySound = playSound;
}
