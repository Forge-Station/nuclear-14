using System.Linq;
using Content.Shared.EntityEffects;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.EffectConditions;

/// <summary>
///     Requires that the metabolizing entity is or is not one of the specified species.
/// </summary>
public sealed partial class Species : EntityEffectCondition
{
    [DataField("species", required: true)]
    public List<ProtoId<SpeciesPrototype>> SpeciesList = new();

    /// <summary>
    ///     Does this condition pass when the entity is one of the species, or when it isn't?
    /// </summary>
    [DataField]
    public bool ShouldHave = true;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out HumanoidAppearanceComponent? humanoid))
            return !ShouldHave;

        var isSpecies = SpeciesList.Contains(humanoid.Species);
        return isSpecies == ShouldHave;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        var names = SpeciesList.Select(s => Loc.GetString(prototype.Index<SpeciesPrototype>(s).Name)).ToList();
        return Loc.GetString("reagent-effect-condition-guidebook-species",
            ("species", Content.Shared.Localizations.ContentLocalizationManager.FormatList(names)),
            ("shouldhave", ShouldHave));
    }
}
