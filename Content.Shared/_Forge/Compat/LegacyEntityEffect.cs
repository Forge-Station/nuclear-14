using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects;

/// <summary>
/// Adapter for fork effects written against the pre-merge EntityEffect API.
///
/// Upstream replaced that API with an event-based one: an effect now only declares data
/// and a matching <see cref="EntityEffectSystem{T,TEffect}"/> applies it. This base keeps
/// the old shape - a single <see cref="Effect"/> call with
/// <see cref="EntityEffectBaseArgs"/> - working on top of the new pipeline.
///
/// Upstream's event carries only the scale, so the arguments handed to the effect carry
/// the scale and nothing else: which reagent fired, the source solution and the
/// metabolised quantity are no longer known at the moment the effect runs - upstream
/// expresses them as DataFields on the effect itself. Quantity is therefore passed as 1,
/// which means a fork effect with <c>scaled: true</c> now scales by the event's scale
/// alone instead of by the metabolised amount. Effects that need the real reagent still
/// have to be ported to upstream's model.
/// </summary>
public abstract partial class LegacyEntityEffect : EntityEffect
{
    public override void RaiseEvent(EntityUid target, IEntityEffectRaiser raiser, float scale, EntityUid? user)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();

        // Передаём именно реагентные аргументы: форковые эффекты проверяют их тип,
        // чтобы учесть масштаб. Из рантайма апстрим оставил только Scale, поэтому
        // Quantity здесь всегда 1 - см. замечание о количестве выше.
        Effect(new EntityEffectReagentArgs(
            target,
            entMan,
            organEntity: null,
            source: null,
            quantity: FixedPoint2.New(1),
            reagent: null,
            method: null,
            scale: FixedPoint2.New(scale)));
    }

    /// <summary>
    /// Applies the effect. Same signature the fork's effects used before the merge.
    /// </summary>
    public abstract void Effect(EntityEffectBaseArgs args);

    /// <inheritdoc />
    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => ReagentEffectGuidebookText(prototype, entSys);

    /// <summary>
    /// Guidebook text, under the name the fork's effects override.
    /// </summary>
    protected virtual string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;
}
