using Robust.Shared.Timing;

namespace Content.Server._Forge.PainImmunity;

/// <summary>
///     Holds the active pain-immunity sources for a mob. Sources are reagents being
///     metabolized; the system applies the highest immunity currently active, which
///     percentage-wise removes the movement slowdown caused by damage.
/// </summary>
[RegisterComponent]
public sealed partial class PainImmunityComponent : Component
{
    /// <summary>Currently applied immunity, 0..1.</summary>
    [DataField]
    public float AppliedImmunity;

    /// <summary>
    ///     Active sources keyed by the source (reagent) id.
    /// </summary>
    public Dictionary<string, (float Immunity, TimeSpan Expiry)> Sources = new();
}