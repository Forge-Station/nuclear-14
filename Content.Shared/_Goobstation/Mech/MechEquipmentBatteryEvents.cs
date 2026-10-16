using Robust.Shared.Serialization;

namespace Content.Shared.Mech.EntitySystems;

/// <summary>
///     Goobstation: raised when a user attempts to fire a mech weapon, to check if its battery is drained.
///     Previously declared in SharedMechSystem, which upstream rewrote.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class HandleMechEquipmentBatteryEvent : EntityEventArgs
{
}
