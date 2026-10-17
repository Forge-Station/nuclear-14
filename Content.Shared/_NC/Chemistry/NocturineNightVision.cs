using Content.Shared.EntityEffects;
using Content.Shared.Overlays;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;

namespace Content.Shared.Chemistry.Effects;

/// <inheritdoc cref="EntityEffectSystem{T, TEffect}"/>
public sealed partial class NocturineNightVisionEntityEffectSystem
    : EntityEffectSystem<MetaDataComponent, NocturineNightVision>
{
    [Dependency] private StatusEffectsSystem _status = default!;

    protected override void Effect(Entity<MetaDataComponent> entity, ref EntityEffectEvent<NocturineNightVision> args)
    {
        var effect = args.Effect;
        var duration = TimeSpan.FromSeconds(effect.DurationSeconds * args.Scale);

        if (!_status.TryAddStatusEffectDuration(entity, effect.StatusEffect, out var statusEffect, duration))
            return;

        // Красит экран в цвет, заданный реагентом: сам эффект транслируется носителю
        // через relayOverlay, поэтому цвет живёт на сущности эффекта.
        if (!TryComp<NightVisionComponent>(statusEffect, out var nightVision)
            || nightVision.OverlayColor == effect.NightVisionColor)
            return;

        nightVision.OverlayColor = effect.NightVisionColor;
        Dirty(statusEffect.Value, nightVision);
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

    /// <summary>
    /// Статус-эффект, который выдаётся на время действия реагента.
    /// </summary>
    [DataField]
    public EntProtoId StatusEffect = "StatusEffectNightVision";

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-nocturine-night-vision", ("time", DurationSeconds));
}
