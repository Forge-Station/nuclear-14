using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Armor;

/// <summary>
///     Marks the finished forged Legion armor.
///     When the armor is worn it automatically fills the gloves slot with
///     power armor gauntlets and drops any previously worn gloves. The gauntlets
///     are a single persistent entity owned by the armor: they are stored in the
///     armor's internal container while it is not worn and cannot be removed manually.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgePowerArmorComponent : Component
{
    /// <summary>
    ///     The gauntlets entity owned by this armor. Stays with the armor for its whole lifetime.
    /// </summary>
    [ViewVariables, NonSerialized]
    public EntityUid? Gauntlets;

    /// <summary>
    ///     Container that holds the gauntlets while the armor is not worn.
    /// </summary>
    [ViewVariables, NonSerialized]
    public ContainerSlot? Container;
}

/// <summary>
///     Marks the power armor gauntlets issued by <see cref="N14ForgePowerArmorComponent"/>.
///     Prevents the gauntlets from being unequipped while their owner armor exists.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeGauntletComponent : Component
{
    /// <summary>
    ///     The armor entity that owns these gauntlets.
    /// </summary>
    [ViewVariables, NonSerialized]
    public EntityUid Owner;
}