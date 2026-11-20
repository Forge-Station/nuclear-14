using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.ChemEffects;

/// <summary>
///     While the source reagent is being metabolized, multiplies the target's
///     maximum stamina (via StaminaModifier). Restores the original value afterwards.
/// </summary>
public sealed partial class ModifyStaminaModifier : EntityEffect
{
    [DataField]
    public float Modifier = 1.5f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Reagent is null)
            return;

        args.EntityManager.System<TimedStaminaModifierSystem>().Refresh(
            args.TargetEntity,
            reagentArgs.Reagent.ID,
            Modifier);
    }
}
