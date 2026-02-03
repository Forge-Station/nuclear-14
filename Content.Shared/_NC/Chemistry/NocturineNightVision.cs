using Content.Shared.Chemistry.Components;
using Content.Shared.EntityEffects;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;

namespace Content.Shared.Chemistry.Effects;

[DataDefinition]
public sealed partial class NocturineNightVision : EntityEffect
{
    [DataField("durationSeconds")] public float DurationSeconds = 2.0f;
    [DataField("color")] public Color NightVisionColor = Color.Green;

    protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager sys) => Loc.GetString("reagent-effect-guidebook-nocturine-night-vision", ("time", DurationSeconds));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs)
            return;

        var uid = args.TargetEntity;
        var entMan = args.EntityManager;

        var statusSys = entMan.System<StatusEffectsSystem>();

        if (!statusSys.TryAddStatusEffect<NocturineNightVisionStatusEffectComponent>(
            uid,
            NocturineNightVisionStatusEffectSystem.StatusKey,
            TimeSpan.FromSeconds(DurationSeconds),
            refresh: true))
            return;
        entMan.System<NocturineNightVisionStatusEffectSystem>()
            .Refresh(uid, NightVisionColor);
    }

}
