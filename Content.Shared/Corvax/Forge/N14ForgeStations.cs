using Content.Shared.Corvax.Forge;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.Forge;

/// <summary>
///     The forge furnace (печь). Accepts a clean bloom and cooks it into a plate (тарелка).
///     Slot-based with a YAML-configured cook time.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeFurnaceComponent : Component
{
    /// <summary>
    ///     Container slot that holds the bloom being cooked.
    /// </summary>
    [ViewVariables]
    public ContainerSlot Slot = default!;

    /// <summary>
    ///     How long a bloom takes to turn into a plate, in seconds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CookTime = 20f;

    /// <summary>
    ///     Coal units consumed per cook. The coal burns off gradually during the cook.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CoalCost = 5;

    /// <summary>
    ///     Maximum coal units the fuel bunker can hold.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaxCoal = 100;

    /// <summary>
    ///     Container slot with the coal that fuels the furnace.
    /// </summary>
    [ViewVariables]
    public ContainerSlot FuelSlot = default!;

    /// <summary>
    ///     Coal units already consumed by the current cook (server-side).
    /// </summary>
    [ViewVariables]
    public int FuelUsedThisCook;

    /// <summary>
    ///     Which prototype is spawned once the cook finishes. Depends on what was inserted.
    /// </summary>
    [ViewVariables]
    public string ResultProto = "N14ForgePlate";

    /// <summary>
    ///     When the current cook finishes (server-side), null when idle.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public TimeSpan? EndTime;

    /// <summary>
    ///     Whether the furnace is currently cooking.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Cooking;
}

/// <summary>
///     The gorn (горн). Accepts plates to heat them up; a heated plate
///     is then quenched in water to become a forged/tempered plate.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14ForgeGornComponent : Component
{
    [ViewVariables]
    public ContainerSlot Slot = default!;

    [DataField, AutoNetworkedField]
    public float HeatTime = 20f;

    /// <summary>
    ///     Coal units consumed per heating. The coal burns off gradually during heating.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CoalCost = 5;

    /// <summary>
    ///     Maximum coal units the fuel bunker can hold.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaxCoal = 100;

    /// <summary>
    ///     Container slot with the coal that fuels the gorn.
    /// </summary>
    [ViewVariables]
    public ContainerSlot FuelSlot = default!;

    /// <summary>
    ///     Coal units already consumed by the current heating (server-side).
    /// </summary>
    [ViewVariables]
    public int FuelUsedThisCook;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan? EndTime;

    [ViewVariables, AutoNetworkedField]
    public bool Heating;
}

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
}

[Serializable, NetSerializable]
public enum N14ForgeFurnaceVisuals : byte
{
    Cooking,
}

[Serializable, NetSerializable]
public enum CookingVisualState : byte
{
    Idle,
    Cooking,
}