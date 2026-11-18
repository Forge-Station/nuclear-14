using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch.Prototypes;

[Prototype("randomResearchSheet")]
public sealed partial class RandomResearchSheetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<ProtoId<LatheRecipePrototype>> Recipes = new();

    /// <summary>
    /// Entity prototype spawned when this pool is printed. It carries a
    /// <c>RandomBlueprintComponent</c> that rolls one of <see cref="Recipes"/> on init.
    /// </summary>
    [DataField]
    public EntProtoId? Sheet;
}
