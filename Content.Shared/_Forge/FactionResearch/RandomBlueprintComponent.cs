using Content.Shared._Misfits.FactionResearch.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.FactionResearch;

/// <summary>
/// Marks a blueprint item whose unlocked recipe is rolled randomly from a
/// <see cref="RandomResearchSheetPrototype"/> pool the first time it spawns.
/// Replaces the old approach of generating one entity prototype per recipe.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RandomBlueprintComponent : Component
{
    /// <summary>
    /// The pool of recipes to roll from.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<RandomResearchSheetPrototype> Pool;

    /// <summary>
    /// The rolled recipe(s). Assigned on map init if empty; synced to clients.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<LatheRecipePrototype>>? Recipes;
}
