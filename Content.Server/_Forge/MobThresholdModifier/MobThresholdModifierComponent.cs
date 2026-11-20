using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Server._Forge.MobThresholdModifier;

/// <summary>
///     Tracks active sources that temporarily modify a mob's damage thresholds:
///     "crit" raises the threshold at which the mob enters Critical,
///     "death" raises the death threshold. Sources are reagents being metabolized;
///     the system applies the highest modifier currently active per group.
/// </summary>
[RegisterComponent]
public sealed partial class MobThresholdModifierComponent : Component
{
    /// <summary>
    ///     Original thresholds, captured when the first source appears, used to
    ///     restore the thresholds when all sources expire.
    /// </summary>
    [DataField]
    public FixedPoint2 BaseCrit;

    [DataField]
    public FixedPoint2 BaseDead;

    /// <summary>Currently applied thresholds, to avoid redundant updates.</summary>
    [DataField]
    public FixedPoint2 AppliedCrit;

    [DataField]
    public FixedPoint2 AppliedDead;

    /// <summary>
    ///     Active "crit" sources, keyed by reagent id.
    /// </summary>
    public Dictionary<string, (int Modifier, TimeSpan Expiry)> CritSources = new();

    /// <summary>
    ///     Active "death" sources, keyed by reagent id.
    /// </summary>
    public Dictionary<string, (int Modifier, TimeSpan Expiry)> DeadSources = new();
}
