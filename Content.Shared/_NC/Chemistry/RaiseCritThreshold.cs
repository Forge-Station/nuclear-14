using Content.Server._NC.Chemistry.Components;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffect;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class RaiseCritThreshold : EntityEffect
{
    [DataField]
    public int ThresholdModifier = 20;

    /// <summary>
    /// На сколько поднять порог смерти. По умолчанию равен ThresholdModifier,
    /// чтобы сохранить зазор между критом и смертью.
    /// </summary>
    [DataField]
    public int DeadThresholdModifier = -1;

    [DataField]
    public float StatusLifetime = 30f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-raise-crit-threshold",
            ("chance", Probability),
            ("modifier", ThresholdModifier),
            ("time", StatusLifetime));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var em = args.EntityManager;
        var uid = args.TargetEntity;
        var isFirst = !em.HasComponent<CritThresholdMetabolismComponent>(uid);

        var lifetime = GetScaledLifetime(args);
        if (!em.System<StatusEffectsSystem>().TryAddStatusEffect<CritThresholdMetabolismComponent>(
                uid, "CritThresholdRaised", TimeSpan.FromSeconds(lifetime), true))
            return;

        if (isFirst)
            ApplyModifiers(em, uid);
    }

    private float GetScaledLifetime(EntityEffectBaseArgs args)
    {
        var lifetime = StatusLifetime;
        if (args is EntityEffectReagentArgs reagentArgs)
            lifetime *= reagentArgs.Scale.Float();
        return lifetime;
    }

    private void ApplyModifiers(IEntityManager em, EntityUid uid)
    {
        if (!em.TryGetComponent<CritThresholdMetabolismComponent>(uid, out var comp)
            || !em.TryGetComponent<MobThresholdsComponent>(uid, out var thresholds))
            return;

        var deadMod = DeadThresholdModifier < 0 ? ThresholdModifier : DeadThresholdModifier;
        comp.CritModifier = ThresholdModifier;
        comp.DeadModifier = deadMod;

        var sys = em.System<MobThresholdSystem>();
        ShiftThreshold(sys, uid, Shared.Mobs.MobState.Critical, ThresholdModifier, thresholds);
        ShiftThreshold(sys, uid, Shared.Mobs.MobState.Dead, deadMod, thresholds);
    }

    private static void ShiftThreshold(MobThresholdSystem sys, EntityUid uid,
        Shared.Mobs.MobState state, int delta, MobThresholdsComponent thresholds)
    {
        var current = sys.GetThresholdForState(uid, state, thresholds);
        if (current != FixedPoint2.Zero)
            sys.SetMobStateThreshold(uid, current + delta, state, thresholds);
    }
}