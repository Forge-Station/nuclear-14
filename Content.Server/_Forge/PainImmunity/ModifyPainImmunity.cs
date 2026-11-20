using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.PainImmunity;

/// <summary>
///     While the source reagent is being metabolized, percentage-wise removes the
///     movement slowdown caused by damage. Multiple sources with this effect may be
///     active; only the highest immunity takes effect.
/// </summary>
public sealed partial class ModifyPainImmunity : EntityEffect
{
    /// <summary>
    ///     Fraction of the damage slowdown to remove, 0..1.
    ///     1 = full immunity (no slowdown), 0.5 = half of the slowdown removed.
    /// </summary>
    [DataField]
    public float PainImmunity = 0;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Reagent is null)
            return;

        args.EntityManager.System<PainImmunitySystem>().Refresh(
            args.TargetEntity,
            reagentArgs.Reagent.ID,
            Math.Clamp(PainImmunity, 0f, 1f));
    }
}