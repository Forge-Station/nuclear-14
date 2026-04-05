using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using System;

namespace Content.Server.ThresholdModifier.Effects;

public sealed partial class ThresholdModifierEffect : EntityEffect
{
    [DataField("critMultiplier")]
    public float CritMultiplier = 1f;

    [DataField("deathMultiplier")]
    public float DeathMultiplier = 1f;

    [DataField("critThreshold")]      // абсолютное значение (опционально)
    public FixedPoint2? CritThreshold;

    [DataField("deathThreshold")]     // абсолютное значение (опционально)
    public FixedPoint2? DeathThreshold;

    [DataField("duration")]
    public float Duration = 10f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (CritThreshold.HasValue || DeathThreshold.HasValue)
        {
            return Loc.GetString("reagent-effect-threshold-modifier-absolute",
                ("critThreshold", CritThreshold),
                ("deathThreshold", DeathThreshold),
                ("duration", Duration));
        }
        return Loc.GetString("reagent-effect-threshold-modifier",
            ("critMultiplier", CritMultiplier),
            ("deathMultiplier", DeathMultiplier),
            ("duration", Duration));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs)
            return;

        var system = args.EntityManager.System<ThresholdModifierSystem>();
        system.ApplyTemporaryModifier(
            args.TargetEntity,
            CritMultiplier,
            DeathMultiplier,
            CritThreshold,
            DeathThreshold,
            TimeSpan.FromSeconds(Duration));
    }
}