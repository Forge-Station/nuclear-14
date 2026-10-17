using Robust.Shared.Prototypes;

namespace Content.Shared.Traits;


/// <summary>
///     A prototype defining a valid category for <see cref="TraitPrototype"/>s to go into.
/// </summary>
[Prototype("traitCategory")]
public sealed partial class TraitCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    public const string Default = "Default";

    /// <summary>
    ///     Name of the trait category displayed in the UI.
    /// </summary>
    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    ///     The maximum number of trait points that can be spent in this category.
    /// </summary>
    [DataField]
    public int? MaxTraitPoints;

    [DataField]
    public bool Root;

    [DataField]
    public List<ProtoId<TraitCategoryPrototype>> SubCategories = new();
}
