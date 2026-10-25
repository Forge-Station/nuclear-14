using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Metabolism;

/// <summary>
///     Passes when the metabolizing body (the mob, not the organ) has the given component.
/// </summary>
[UsedImplicitly]
public sealed partial class HasBodyComponent : EntityEffectCondition
{
    [DataField(required: true)]
    public string Component = default!;

    [DataField]
    public bool Invert;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        var factory = IoCManager.Resolve<IComponentFactory>();
        if (!factory.TryGetRegistration(Component, out var registration))
            return false;

        return args.EntityManager.HasComponent(args.TargetEntity, registration.Type) ^ Invert;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
        => string.Empty;
}
