using Content.Shared.Corvax.Forge.Anvil;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.Forge.Dummy;

/// <summary>
///     The mannequin (манекен). Holds up to 7 forge parts: 5 armor pieces + top &amp; bottom decorations.
///     When all slots are filled simply crafted armor (or tempered armor if all parts are tempered) can be claimed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeDummyComponent : Component
{
    public EntityUid? ArmRight;
    public EntityUid? ArmLeft;
    public EntityUid? LegRight;
    public EntityUid? LegLeft;
    public EntityUid? Torso;
    public EntityUid? DecorationTop;
    public EntityUid? DecorationBottom;

    /// <summary>
    ///     Whether mixed tempered/normal parts are not allowed. Locked once the first armor part is mounted,
    ///     all subsequent armor parts must match.
    /// </summary>
    public bool? TemperedLock;

    public N14ForgeArmorSet? SetLock;

    /// <summary>
    ///     The player who started the armor-assembly minigame and should receive the finished armor.
    ///     Null when no minigame is in progress.
    /// </summary>
    public EntityUid? MiniGameUser;

    /// <summary>
    ///     Whether all 7 required slots are filled.
    /// </summary>
    public bool IsComplete =>
        ArmRight != null && ArmLeft != null && LegRight != null && LegLeft != null &&
        Torso != null && DecorationTop != null && DecorationBottom != null;

    /// <summary>
    ///     Clears every mounted slot (5 armor pieces + top &amp; bottom decorations) at once.
    /// </summary>
    public void ClearSlots()
    {
        ArmRight = null;
        ArmLeft = null;
        LegRight = null;
        LegLeft = null;
        Torso = null;
        DecorationTop = null;
        DecorationBottom = null;
    }
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
///     Marks an armor part that has had leather strapped to it,
///     so it can be mounted on the mannequin.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class N14ForgeStrappedComponent : Component
{
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

[Serializable, NetSerializable]
public enum N14ForgePartKind : byte
{
    Arm,
    Leg,
    Torso,
}