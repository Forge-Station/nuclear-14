using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.Forge;

/// <summary>
///     DoAfter for welding 4 flat plates into a torso plate.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class N14ForgeWeldDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>
///     DoAfter for strapping leather to a forged armor part so it can be mounted on the mannequin.
///     Consumes one leather when finished.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class N14ForgeStrapLeatherDoAfterEvent : SimpleDoAfterEvent
{
}