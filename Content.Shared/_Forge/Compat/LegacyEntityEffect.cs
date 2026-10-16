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
/// It can only supply the base arguments. Effects that relied on the runtime reagent
/// context (<c>EntityEffectReagentArgs</c>: which reagent fired, the source solution,
/// the metabolised quantity) cannot use this and need porting to upstream's model, where
/// the reagent and quantity are DataFields on the effect and only Scale comes from the event.
/// </summary>
public abstract partial class LegacyEntityEffect : EntityEffect
{
    public override void RaiseEvent(EntityUid target, IEntityEffectRaiser raiser, float scale, EntityUid? user)
    {
        Effect(new EntityEffectBaseArgs(target, IoCManager.Resolve<IEntityManager>()));
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
