using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.Forge;

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


[Serializable, NetSerializable]
public enum N14ForgePartSide : byte
{
    Right,
    Left,
}

[Serializable, NetSerializable]
public enum N14ForgePartStage : byte
{
    /// <summary>Finished/ready state in hand.</summary>
    Icon,
    /// <summary>Being forged on anvil, hit 1.</summary>
    OnAnvil,
    /// <summary>Being forged on anvil, hit 2.</summary>
    OnAnvil2,
    /// <summary>Mounted on the mannequin.</summary>
    OnDummy,
}

/// <summary>
///     Appearance key + layer for the armor part GenericVisualizer.
/// </summary>
[Serializable, NetSerializable]
public enum N14ForgePartVisuals : byte
{
    Stage,
}

[Serializable, NetSerializable]
public enum N14ForgePartVisualLayer : byte
{
    Layer,
}

/// <summary>
///     Heated marker applied when a plate/part is heated in the gorn.
///     Clicking a heated item on water forges it (forged variants).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeHeatedComponent : Component
{
}

/// <summary>
///     Marks a forged (закалённый) plate part produced by quenching a heated plate in water.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeForgedComponent : Component
{
}

/// <summary>
///     Decoration piece top/bottom for the mannequin.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeDecorationComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Top;
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

/// <summary>
///     Marks an armor part that has had leather strapped to it,
///     so it can be mounted on the mannequin.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeStrappedComponent : Component
{
}

/// <summary>
///     Marks an armor piece part (рука/нога/торс) and its on-anvil forging stage.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeArmorPartComponent : Component
{
    /// <summary>
    ///     Which limb this part belongs to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public N14ForgePartKind Kind;

    /// <summary>
    ///     Which side for limbs (arms/legs). Ignored for torso.
    /// </summary>
    [DataField, AutoNetworkedField]
    public N14ForgePartSide Side;

    /// <summary>
    ///     Whether this is a tempered (закалённая) piece produced from a forged plate.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Tempered;

    /// <summary>
    ///     Forging stage: Icon(ready) -> OnAnvil -> OnAnvil2 -> [after removal from anvil] OnDummy.
    /// </summary>
    [DataField, AutoNetworkedField]
    public N14ForgePartStage Stage;

    /// <summary>
    ///     Progress of sledgehammer hits while the part is on the anvil (0..2 -> OnAnvil,OnAnvil2 then forged finish).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int HammerHits;
}

[Serializable, NetSerializable]
public enum N14ForgePartKind : byte
{
    Arm,
    Leg,
    Torso,
}