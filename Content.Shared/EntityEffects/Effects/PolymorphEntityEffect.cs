using System.Linq;
using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects.Effects;

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class Polymorph : EntityEffectBase<Polymorph>
{
    /// <summary>
    /// Polymorph configuration to apply.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<PolymorphPrototype> Prototype;

    /// <summary>
    /// Allows polymorphing entities without a PolymorphableComponent.
    /// Cooldowns and restrictions on repeated polymorphs still apply.
    /// </summary>
    [DataField]
    public bool Force;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("entity-effect-guidebook-make-polymorph",
            ("chance", Probability),
            ("entityname", GetEntityName(prototype)));

    /// <summary>
    /// Forge-Change: прототип полиморфа может задавать не одну сущность, а список
    /// случайных, и тогда Configuration.Entity пуст - без этого гайдбук падает.
    /// </summary>
    private string GetEntityName(IPrototypeManager prototype)
    {
        var configuration = prototype.Index(Prototype).Configuration;

        if (!string.IsNullOrEmpty(configuration.Entity))
            return prototype.Index<EntityPrototype>(configuration.Entity).Name;

        if (configuration.RandomEnt is not { Count: > 0 } random)
            return Loc.GetString("entity-effect-guidebook-make-polymorph-unknown");

        var names = random
            .Where(entry => entry.PrototypeId != null)
            .Select(entry => prototype.Index<EntityPrototype>(entry.PrototypeId!).Name)
            .Distinct()
            .ToList();

        return names.Count switch
        {
            0 => Loc.GetString("entity-effect-guidebook-make-polymorph-unknown"),
            1 => names[0],
            _ => Loc.GetString("entity-effect-guidebook-make-polymorph-random",
                ("names", string.Join(" / ", names))),
        };
    }
}
