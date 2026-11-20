using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.ChemEffects;

/// <summary>
///     While the source reagent is being metabolized, multiplies the stamina cost
///     of the target's heavy melee attacks. Values below 1 make attacks cheaper.
/// </summary>
public sealed partial class ModifyMeleeStaminaCost : EntityEffect
{
    [DataField]
    public float Modifier = 0.75f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Reagent is null)
            return;

        args.EntityManager.System<MeleeStaminaCostModifierSystem>().Refresh(
            args.TargetEntity,
            reagentArgs.Reagent.ID,
            Modifier);
    }
}
