using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.Forge.Welding;

/// <summary>
///     DoAfter for welding 4 flat plates into a torso plate.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class N14ForgeWeldDoAfterEvent : SimpleDoAfterEvent
{
}