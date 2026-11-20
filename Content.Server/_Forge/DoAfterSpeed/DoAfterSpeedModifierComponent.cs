using Content.Shared.DoAfter;
using Robust.Shared.Timing;

namespace Content.Server._Forge.DoAfterSpeed;

/// <summary>
///     Holds active speed-modifier sources for a mob's DoAfters. Sources are reagents
///     being metabolized; for each DoAfter event type the highest speed currently active
///     is applied (speed &gt; 1 = faster, &lt; 1 = slower). The modified
///     <c>DoAfterArgs.Delay</c> is itself networked to the client.
/// </summary>
[RegisterComponent]
public sealed partial class DoAfterSpeedModifierComponent : Component
{
    /// <summary>
    ///     Active sources per DoAfter event type, keyed by event type name, then reagent id.
    ///     Each entry holds the speed multiplier and when it expires.
    /// </summary>
    public Dictionary<string, Dictionary<string, (float Speed, TimeSpan Expiry)>> Sources = new();

    /// <summary>
    ///     Original (unmodified) delay per active DoAfter, captured the first time the
    ///     modifier is applied, so it can be restored when the effect expires.
    /// </summary>
    public Dictionary<DoAfterId, TimeSpan> BaseDelays = new();
}