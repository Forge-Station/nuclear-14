using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Chemistry.Components;

[RegisterComponent]
public sealed partial class ChemDamageProtectionComponent : Component
{
    [ViewVariables]
    public readonly Dictionary<string, ProtectionSource> Sources = new();

    [ViewVariables, NonSerialized]
    public readonly DamageModifierSet CachedCombined = new();

    [ViewVariables, NonSerialized]
    public bool Dirty;

    [ViewVariables, NonSerialized]
    public TimeSpan NextPruneAt;

    public readonly record struct ProtectionSource(
        ProtoId<DamageModifierSetPrototype> ModifierSetId,
        TimeSpan ExpiresAt
    );
}

[RegisterComponent]
public sealed partial class ChemDamageProtectionStatusComponent : Component
{
}
