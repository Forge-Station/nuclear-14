using Robust.Shared.GameStates;

namespace Content.Shared._Forge.DoAfter;

/// <summary>
///     Scales the delay of tool and medical DoAfters performed by this entity.
///     1 = normal, 2 = twice as fast, 0.5 = twice as slow.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DoAfterSpeedModifierComponent : Component
{
    /// <summary>
    ///     Speed for tool work: crowbar, wrench, screwdriver, wirecutters, drill, welder, etc.
    /// </summary>
    [DataField]
    public float ToolSpeed = 1f;

    /// <summary>
    ///     Speed for medical consumables and injections: medkits/bandages, syringes, defibrillator.
    /// </summary>
    [DataField]
    public float MedicalSpeed = 1f;
}
