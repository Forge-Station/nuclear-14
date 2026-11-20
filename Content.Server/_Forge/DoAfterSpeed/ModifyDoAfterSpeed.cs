using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.DoAfterSpeed;

/// <summary>
///     While the source reagent is being metabolized, speeds up or slows down the
///     target's DoAfters. Each entry maps a DoAfter event type name to a speed
///     multiplier (speed &gt; 1 = faster, &lt; 1 = slower). Only the selected DoAfter
///     event types are affected, and the highest speed among active sources wins.
/// </summary>
public sealed partial class ModifyDoAfterSpeed : EntityEffect
{
    /// <summary>
    ///     DoAfter event type name -&gt; speed multiplier.
    ///     E.g. <c>HealingDoAfterEvent: 2.0</c> makes healing take half the time.
    /// </summary>
    [DataField]
    public Dictionary<string, float> Speeds = new();

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Reagent is null)
            return;

        args.EntityManager.System<DoAfterSpeedModifierSystem>().Refresh(
            args.TargetEntity,
            reagentArgs.Reagent.ID,
            Speeds);
    }
}