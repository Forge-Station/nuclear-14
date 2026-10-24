using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Metabolism;

/// <summary>
///     Skips HealthChange damage of the listed types from the listed reagents,
///     while all other metabolism effects (satiety, etc.) still apply.
/// </summary>
[RegisterComponent]
public sealed partial class MetabolismDamageImmunityComponent : Component
{
    [DataField]
    public List<ProtoId<ReagentPrototype>> Reagents = new();

    [DataField]
    public List<string> DamageTypes = new();
}
