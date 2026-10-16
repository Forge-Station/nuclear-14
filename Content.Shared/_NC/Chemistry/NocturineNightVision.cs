using Content.Shared.EntityEffects;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;

namespace Content.Shared.Chemistry.Effects;

/// <inheritdoc cref="EntityEffectSystem{T, TEffect}"/>
public sealed partial class NocturineNightVisionEntityEffectSystem
    : EntityEffectSystem<MetaDataComponent, NocturineNightVision>
{
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private NocturineNightVisionStatusEffectSystem _nightVision = default!;

    protected override void Effect(Entity<MetaDataComponent> entity, ref EntityEffectEvent<NocturineNightVision> args)
    {
        var effect = args.Effect;

        if (!_status.TryAddStatusEffect<NocturineNightVisionStatusEffectComponent>(
                entity,
                NocturineNightVisionStatusEffectSystem.StatusKey,
                TimeSpan.FromSeconds(effect.DurationSeconds),
                refresh: true))
            return;

        _nightVision.Refresh(entity, effect.NightVisionColor);
    }
}

/// <inheritdoc cref="EntityEffect"/>
[DataDefinition]
public sealed partial class NocturineNightVision : EntityEffectBase<NocturineNightVision>
{
    [DataField("durationSeconds")]
    public float DurationSeconds = 2.0f;

    [DataField("color")]
    public Color NightVisionColor = Color.Green;

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-nocturine-night-vision", ("time", DurationSeconds));
}
