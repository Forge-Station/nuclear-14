using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Misfits.Crafting;

/// <summary>
/// When an entity carrying this component is placed inside a workbench's
/// storage container, the listed lathe recipes become available on that
/// workbench. Removing the blueprint hides them again.
///
/// This replaces the removed Stalker14 STBlueprintComponent with a clean,
/// original implementation that directly references lathe recipe IDs.
/// </summary>
[RegisterComponent]
public sealed partial class BlueprintComponent : Component
{
    /// <summary>
    /// Lathe recipe prototype IDs this blueprint unlocks when placed in a
    /// workbench storage container.
    /// </summary>
    [DataField("recipes", required: true)]
    public List<ProtoId<LatheRecipePrototype>> Recipes = new();
}
