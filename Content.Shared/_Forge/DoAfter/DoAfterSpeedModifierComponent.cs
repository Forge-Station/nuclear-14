using Content.Shared.DoAfter;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.DoAfter;

/// <summary>
///     Scales DoAfter delays for this entity.
///     Persistent job/trait fields (<see cref="ToolSpeed"/> / <see cref="MedicalSpeed"/>)
///     are applied when a DoAfter starts. Temporary chem <see cref="Sources"/> rewrite
///     active delays by event type while metabolized.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DoAfterSpeedModifierComponent : Component
{
    /// <summary>
    ///     Speed for tool work: crowbar, wrench, screwdriver, wirecutters, drill, welder, etc.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ToolSpeed = 1f;

    /// <summary>
    ///     Speed for medical consumables and injections: medkits/bandages, syringes, defibrillator.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MedicalSpeed = 1f;

    /// <summary>
    ///     Active chem sources per DoAfter event type, keyed by event type name, then reagent id.
    ///     Server-only; not networked.
    /// </summary>
    public Dictionary<string, Dictionary<string, (float Speed, TimeSpan Expiry)>> Sources = new();

    /// <summary>
    ///     Original (unmodified) delay per active DoAfter, captured the first time a chem
    ///     modifier is applied, so it can be restored when the effect expires.
    /// </summary>
    public Dictionary<DoAfterId, TimeSpan> BaseDelays = new();
}
