using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Anvil;

/// <summary>
///     Lives on the anvil. Tracks how many arms/legs have been forged
///     so that the first forged part is always the right one, then left one, etc.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeAnvilComponent : Component
{
    /// <summary>
    ///     Count of forged arm parts. Even index -> right arm, odd index -> left arm.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ArmsForged;

    /// <summary>
    ///     Count of forged leg parts. Even index -> right leg, odd index -> left leg.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int LegsForged;
}

/// <summary>
///     A bloom (крица). Dirty blooms are hammered on the anvil until clean,
///     hammer progress is stored here.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeBloomComponent : Component
{
    /// <summary>
    ///     Whether this bloom is already clean (варёная крица, ready for the furnace).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Clean;

    /// <summary>
    ///     Which armor set this bloom belongs to. Used to smelt it into the matching plate.
    /// </summary>
    [DataField, AutoNetworkedField]
    public N14ForgeArmorSet Set;
}

/// <summary>
///     Hammer progress of a dirty bloom on the anvil. 0-100.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeBloomProgressComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Progress;
}

/// <summary>
///     Marks a plate (a flat piece of forged metal) and its stage.
///     Plain plate -> furnace result used for welding; torso/arm/leg plates are forged on the anvil.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgePlateComponent : Component
{
    /// <summary>
    ///     Type of the plate.
    /// </summary>
    [DataField, AutoNetworkedField]
    public N14ForgePlateType PlateType;

    /// <summary>
    ///     Variation of the plate: сваренные -> печь -> кованые -> горн+вода -> закалённые.
    ///     Only Forged/Tempered plates can be forged on the anvil.
    /// </summary>
    [DataField, AutoNetworkedField]
    public N14ForgePlateVariant Variant;

    /// <summary>
    ///     Which armor set's plates this is. Determines the forged/forged-outcome prototypes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public N14ForgeArmorSet Set;
}

/// <summary>
///     Sledgehammer progress of an armor part being forged on the anvil. 0-100.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgePartProgressComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Progress;
}

/// <summary>
///     Marks a crude neck kulon (amulet) forged at the bloomery.
///     On an anvil a blacksmith hammer makes it wearable;
///     a blacksmith sledgehammer after that imbues it with special properties.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeCulonComponent : Component
{
    /// <summary>
    ///     Whether the kulon has been worked with a blacksmith hammer (and can now be worn).
    /// </summary>
    [DataField]
    public bool Worked;
}

/// <summary>
///     Smithing progress of a kulon being worked on the anvil. 0-100.
///     Blacksmith-hammer hits build it up until the crude kulon becomes worked,
///     sledgehammer hits build it up until the worked kulon is imbued.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeCulonProgressComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Progress;
}

[Serializable, NetSerializable]
public enum N14ForgeArmorSet : byte
{
    Monstruum,
    Kratos,
    Bull,
}

[Serializable, NetSerializable]
public enum N14ForgePlateVariant : byte
{
    /// <summary>Plain welded plate, ready to be heated.</summary>
    Welded,
    /// <summary>Forged (кованый) plate.</summary>
    Forged,
    /// <summary>Tempered (закалённый) plate.</summary>
    Tempered,
}

[Serializable, NetSerializable]
public enum N14ForgePlateType : byte
{
    /// <summary>Засваренная тарелка (furnace result), used in welding 4x.</summary>
    Plate,
    /// <summary>Сваренная вертикальная плита (torso plate).</summary>
    TorsoPlate,
    /// <summary>Вертикальная половина (arm).</summary>
    ArmPlate,
    /// <summary>Горизонтальная половина (leg).</summary>
    LegPlate,
}

/// <summary>
///     DoAfter for strapping leather to a forged armor part so it can be mounted on the mannequin.
///     Consumes one leather when finished.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class N14ForgeStrapLeatherDoAfterEvent : SimpleDoAfterEvent
{
}