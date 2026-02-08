using Content.Server.Chemistry.EntitySystems;
using Content.Shared.Damage.Prototypes;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server.Chemistry.Effects;

[DataDefinition]
public sealed partial class ChemDamageProtection : EntityEffect
{
    [DataField] public float DurationSeconds = 10.0f;
    [DataField] public string? Key;

    [DataField(required: true)]
    public ProtoId<DamageModifierSetPrototype> ModifierSet;

    protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString(
            "reagent-effect-guidebook-chem-damage-protection",
            ("chance", Probability),
            ("modifierSet", ModifierSet));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs)
            return;

        var key = (Key ?? reagentArgs.Reagent?.ID)?.Trim();
        if (string.IsNullOrEmpty(key))
            return;

        var duration = DurationSeconds;
        if (float.IsNaN(duration) || duration <= 0f)
            return;

        var sys = args.EntityManager.System<ChemDamageProtectionSystem>();
        sys.AddOrRefresh(args.TargetEntity, key, ModifierSet, TimeSpan.FromSeconds(duration));
    }
}
