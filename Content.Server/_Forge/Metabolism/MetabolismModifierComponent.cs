using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Metabolism;

/// <summary>
///     Lets an entity neutralize or vomit on specific reagents while they metabolize.
/// </summary>
[RegisterComponent]
public sealed partial class MetabolismModifierComponent : Component
{
    /// <summary>
    ///     Reagents that are neutralized (no metabolism effects).
    /// </summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> Neutralize = new();

    /// <summary>
    ///     Reagents that make the entity vomit and are then neutralized.
    /// </summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> Vomit = new();
}
