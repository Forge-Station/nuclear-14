using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Abilities;

/// <summary>
///     This gun may be fired together with another held gun when the shooter is ambidextrous.
///     <see cref="Whitelist"/> on each gun decides which partners are allowed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DualWieldableComponent : Component
{
    /// <summary>
    ///     The other gun must match this list. Checked from both sides.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;
}
