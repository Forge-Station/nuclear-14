using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.MobThresholdModifier;

/// <summary>
///     While the source reagent is being metabolized, temporarily modifies the target's
///     damage thresholds. Each modifier is applied to a different state, and when multiple
///     sources with this effect are active only the largest modifier per state takes effect.
/// </summary>
public sealed partial class ModifyMobThresholds : EntityEffect
{
    /// <summary>
    ///     Modifier applied to the critical threshold.
    /// </summary>
    [DataField]
    public int CritModifier = 0;

    /// <summary>
    ///     Modifier applied to the death threshold.
    /// </summary>
    [DataField]
    public int DeathModifier = 0;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Reagent is null)
            return;

        args.EntityManager.System<MobThresholdModifierSystem>().Refresh(
            args.TargetEntity,
            reagentArgs.Reagent.ID,
            CritModifier,
            DeathModifier);
    }
}
