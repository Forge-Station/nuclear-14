using Content.Shared.EntityEffects;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.EffectConditions;

public sealed partial class Hunger : EntityEffectCondition
{
    [DataField]
    public float Max = float.PositiveInfinity;

    [DataField]
    public float Min = 0;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        // Forge-Change: голод стал сытостью - значение спрашиваем у SatiationSystem.
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out SatiationComponent? satiation))
            return false;

        var satiationSystem = args.EntityManager.System<SatiationSystem>();
        if (satiationSystem.GetValueOrNull((args.TargetEntity, satiation), SatiationSystem.Hunger) is not { } total)
            return false;

        return total > Min && total < Max;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        return Loc.GetString("reagent-effect-condition-guidebook-total-hunger",
            ("max", float.IsPositiveInfinity(Max) ? (float) int.MaxValue : Max),
            ("min", Min));
    }
}
